using System.Data.Common;
using System.Text.Json;
using Model;
using MySql.Data.MySqlClient;

namespace DAL;

public sealed class QuotaExceededException : Exception;
public sealed record AnalysisWork(int Id, string AudioPath, string PromptText, int RetryCount);

public sealed class PracticeDB(string connectionString)
{
    private async Task<MySqlConnection> OpenAsync()
    {
        var connection = new MySqlConnection(connectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { connection.Dispose(); throw; }
    }
    private static MySqlCommand Command(MySqlConnection connection, string sql, params (string, object?)[] values)
    {
        var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
    private static ExerciseSummary ReadExercise(DbDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5));
    public async Task<List<ExerciseSummary>> GetExercisesAsync()
    {
        using var c = await OpenAsync();
        using var command = Command(c, """
            SELECT e.ExerciseId,e.LessonId,l.Title,e.PromptText,e.Instructions,e.MaxSeconds
            FROM Exercises e JOIN Lessons l ON e.LessonId=l.LessonId
            WHERE l.IsPublished=TRUE AND e.IsActive=TRUE ORDER BY l.SortOrder,e.SortOrder,e.ExerciseId
            """);
        using var r = await command.ExecuteReaderAsync();
        var result = new List<ExerciseSummary>();
        while (await r.ReadAsync()) result.Add(ReadExercise(r));
        return result;
    }
    public async Task<ExerciseSummary?> GetExerciseAsync(int id) => (await GetExercisesAsync()).Find(x => x.Id == id);

    private const string AttemptQuery = """
        SELECT p.AttemptId,p.ExerciseId,l.Title,p.DurationSeconds,p.SubmittedAt,p.AnalysisStatus,
        p.Transcript,p.AIFeedback,p.AnalysisError,j.RetryCount
        FROM PracticeAttempts p JOIN Exercises e ON p.ExerciseId=e.ExerciseId
        JOIN Lessons l ON e.LessonId=l.LessonId JOIN AnalysisJobs j ON p.AttemptId=j.AttemptId
        """;
    private static AttemptSummary ReadAttempt(DbDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3),
        DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc), Enum.Parse<AnalysisState>(r.GetString(5)),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : JsonSerializer.Deserialize<LearningFeedback>(r.GetString(7), AppJson.Options),
        r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9));

    public async Task<PracticeHistory> GetHistoryAsync(int studentId, int dailyLimit)
    {
        using var c = await OpenAsync();
        var result = new List<AttemptSummary>();
        using (var command = Command(c, AttemptQuery + " WHERE p.StudentId=@student ORDER BY p.SubmittedAt DESC,p.AttemptId DESC LIMIT 100", ("@student", studentId)))
        using (var r = await command.ExecuteReaderAsync())
            while (await r.ReadAsync()) result.Add(ReadAttempt(r));
        using var usage = Command(c, "SELECT COUNT(*) FROM AnalysisUsage WHERE StudentId=@student AND RequestedAt>=UTC_DATE()", ("@student", studentId));
        return new(result, Convert.ToInt32(await usage.ExecuteScalarAsync()), dailyLimit);
    }
    public async Task<AttemptSummary?> GetAttemptAsync(int studentId, int id)
    {
        using var c = await OpenAsync();
        using var command = Command(c, AttemptQuery + " WHERE p.StudentId=@student AND p.AttemptId=@id", ("@student", studentId), ("@id", id));
        using var r = await command.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadAttempt(r) : null;
    }
    public async Task<int?> FindSubmissionAsync(int studentId, string key)
    {
        using var c = await OpenAsync();
        using var command = Command(c, "SELECT AttemptId FROM PracticeAttempts WHERE StudentId=@student AND SubmissionKey=@key", ("@student", studentId), ("@key", key));
        var value = await command.ExecuteScalarAsync();
        return value is null ? null : Convert.ToInt32(value);
    }
    private static async Task ChargeAsync(MySqlConnection c, MySqlTransaction tx, int studentId, int limit)
    {
        // Serialize submissions and retries for this student, including multiple API instances.
        using var userLock = Command(c, "SELECT UserId FROM Users WHERE UserId=@id FOR UPDATE", ("@id", studentId));
        userLock.Transaction = tx;
        await userLock.ExecuteScalarAsync();
        using var count = Command(c, "SELECT COUNT(*) FROM AnalysisUsage WHERE StudentId=@id AND RequestedAt>=UTC_DATE()", ("@id", studentId));
        count.Transaction = tx;
        if (Convert.ToInt32(await count.ExecuteScalarAsync()) >= limit) throw new QuotaExceededException();
        using var usage = Command(c, "INSERT INTO AnalysisUsage(StudentId,RequestedAt) VALUES(@id,UTC_TIMESTAMP())", ("@id", studentId));
        usage.Transaction = tx;
        await usage.ExecuteNonQueryAsync();
    }
    public async Task<int> SubmitAsync(int studentId, int exerciseId, string key, string audioPath, int seconds, int limit)
    {
        using var c = await OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        await ChargeAsync(c, tx, studentId, limit);
        using var command = Command(c, """
            INSERT INTO PracticeAttempts(StudentId,ExerciseId,SubmissionKey,AudioPath,DurationSeconds,SubmittedAt)
            VALUES(@student,@exercise,@key,@path,@seconds,UTC_TIMESTAMP())
            """, ("@student", studentId), ("@exercise", exerciseId), ("@key", key), ("@path", audioPath), ("@seconds", seconds));
        command.Transaction = tx;
        await command.ExecuteNonQueryAsync();
        int id = checked((int)command.LastInsertedId);
        using var job = Command(c, "INSERT INTO AnalysisJobs(AttemptId) VALUES(@id)", ("@id", id));
        job.Transaction = tx;
        await job.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return id;
    }
    public async Task<bool> RetryAsync(int studentId, int id, int limit)
    {
        using var c = await OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        await ChargeAsync(c, tx, studentId, limit);
        using var command = Command(c, """
            UPDATE PracticeAttempts p JOIN AnalysisJobs j ON p.AttemptId=j.AttemptId
            SET p.AnalysisStatus='Pending',p.AnalysisError=NULL,j.RetryCount=j.RetryCount+1,j.LeaseUntil=NULL
            WHERE p.AttemptId=@id AND p.StudentId=@student AND p.AnalysisStatus='Failed' AND j.RetryCount<2
            """, ("@id", id), ("@student", studentId));
        command.Transaction = tx;
        if (await command.ExecuteNonQueryAsync() == 0) return false; // rollback quota too
        await tx.CommitAsync();
        return true;
    }
    public async Task<string?> GetAudioPathAsync(int studentId, int id)
    {
        using var c = await OpenAsync();
        using var command = Command(c, "SELECT AudioPath FROM PracticeAttempts WHERE StudentId=@student AND AttemptId=@id", ("@student", studentId), ("@id", id));
        return await command.ExecuteScalarAsync() as string;
    }
    public async Task<string?> DeleteAsync(int studentId, int id, Action<string> deleteAudio)
    {
        using var c = await OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        using var select = Command(c, "SELECT AudioPath FROM PracticeAttempts WHERE StudentId=@student AND AttemptId=@id FOR UPDATE", ("@student", studentId), ("@id", id));
        select.Transaction = tx;
        var path = await select.ExecuteScalarAsync() as string;
        if (path is null) return null;
        deleteAudio(path); // Keep the row if filesystem deletion fails; the user can retry.
        using var delete = Command(c, "DELETE FROM PracticeAttempts WHERE AttemptId=@id", ("@id", id));
        delete.Transaction = tx;
        await delete.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return path;
    }
    public async Task<AnalysisWork?> ClaimAsync()
    {
        using var c = await OpenAsync();
        using var tx = await c.BeginTransactionAsync();
        using var command = Command(c, """
            SELECT p.AttemptId,p.AudioPath,e.PromptText,j.RetryCount
            FROM PracticeAttempts p JOIN AnalysisJobs j ON p.AttemptId=j.AttemptId
            JOIN Exercises e ON p.ExerciseId=e.ExerciseId
            WHERE p.AnalysisStatus='Pending' OR (p.AnalysisStatus='Processing' AND j.LeaseUntil<UTC_TIMESTAMP())
            ORDER BY p.SubmittedAt LIMIT 1 FOR UPDATE SKIP LOCKED
            """);
        command.Transaction = tx;
        AnalysisWork? work;
        using (var r = await command.ExecuteReaderAsync())
            work = await r.ReadAsync() ? new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3)) : null;
        if (work is null) return null;
        using var update = Command(c, """
            UPDATE PracticeAttempts p JOIN AnalysisJobs j ON p.AttemptId=j.AttemptId
            SET p.AnalysisStatus='Processing',j.LeaseUntil=DATE_ADD(UTC_TIMESTAMP(), INTERVAL 5 MINUTE) WHERE p.AttemptId=@id
            """, ("@id", work.Id));
        update.Transaction = tx;
        await update.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return work;
    }
    public async Task CompleteAsync(int id, AnalysisState state, string? transcript, LearningFeedback? feedback, string? error)
    {
        using var c = await OpenAsync();
        using var command = Command(c, """
            UPDATE PracticeAttempts SET AnalysisStatus=@state,Transcript=@text,AIFeedback=@feedback,
            AnalysisError=@error,AnalyzedAt=UTC_TIMESTAMP() WHERE AttemptId=@id AND AnalysisStatus='Processing'
            """, ("@id", id), ("@state", state.ToString()), ("@text", transcript),
            ("@feedback", feedback is null ? null : JsonSerializer.Serialize(feedback, AppJson.Options)), ("@error", error));
        await command.ExecuteNonQueryAsync();
    }
    public async Task SeedContentAsync()
    {
        using var c = await OpenAsync();
        // One advisory lock prevents duplicate content on concurrent deployments. Never alter existing lessons.
        using var acquire = Command(c, "SELECT GET_LOCK('SpeakReadyPilotContent',30)");
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("Content seed lock unavailable.");
        try
        {
            using var tx = await c.BeginTransactionAsync();
            using var author = Command(c, """
                INSERT INTO Users(Username,FullName,Email,PasswordHash,Role,IsActive)
                VALUES('speakready-content','SpeakReady','content@speakready.invalid','disabled','Admin',FALSE)
                ON DUPLICATE KEY UPDATE UserId=LAST_INSERT_ID(UserId)
                """);
            author.Transaction = tx;
            await author.ExecuteNonQueryAsync();
            long authorId = author.LastInsertedId;
            var items = new[] {
                ("self-introduction", "קצת על עצמי", "Tell us about yourself and something you enjoy doing in your free time.", "הציגו את עצמכם, ספרו על תחביב והוסיפו סיבה ודוגמה."),
                ("past-experience", "חוויה מהעבר", "Describe a memorable experience. What happened, and why was it important to you?", "ספרו מה קרה, איך הרגשתם ולמה החוויה משמעותית עבורכם."),
                ("personal-opinion", "הדעה שלי", "Do you think students should have more outdoor lessons? Explain your opinion with reasons and an example.", "בחרו עמדה, נמקו אותה והוסיפו דוגמה שתומכת בה.")
            };
            int order = 0;
            foreach (var (key, title, prompt, instructions) in items)
            {
                order++;
                using var exists = Command(c, "SELECT LessonId FROM PilotContent WHERE ContentKey=@key", ("@key", key));
                exists.Transaction = tx;
                if (await exists.ExecuteScalarAsync() is not null) continue;
                using var lesson = Command(c, "INSERT INTO Lessons(Title,Content,SortOrder,IsPublished,CreatedByUserId) VALUES(@title,@content,@sort,TRUE,@author)",
                    ("@title", title), ("@content", instructions), ("@sort", order), ("@author", authorId));
                lesson.Transaction = tx;
                await lesson.ExecuteNonQueryAsync();
                long lessonId = lesson.LastInsertedId;
                using var exercise = Command(c, "INSERT INTO Exercises(LessonId,PromptText,Instructions,MaxSeconds) VALUES(@lesson,@prompt,@instructions,120)",
                    ("@lesson", lessonId), ("@prompt", prompt), ("@instructions", instructions));
                exercise.Transaction = tx;
                await exercise.ExecuteNonQueryAsync();
                using var marker = Command(c, "INSERT INTO PilotContent(ContentKey,LessonId) VALUES(@key,@lesson)", ("@key", key), ("@lesson", lessonId));
                marker.Transaction = tx;
                await marker.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        finally { using var release = Command(c, "SELECT RELEASE_LOCK('SpeakReadyPilotContent')"); await release.ExecuteScalarAsync(); }
    }
}
