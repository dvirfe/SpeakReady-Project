using System.Text.Json;
using System.Text.Json.Serialization;

namespace Model;

public static class AppJson
{
    public static readonly JsonSerializerOptions Options = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public record LoginResult(User User, string Token, DateTime ExpiresAt);
public record ExerciseSummary(int Id, int LessonId, string Title, string PromptText, string Instructions, int MaxSeconds);
public record Correction(string Original, string Improved, string Explanation);
public record LearningFeedback(string[] Strengths, string[] Improvements, Correction[] Corrections, string NextStep);
public record AttemptSummary(int Id, int ExerciseId, string Title, int DurationSeconds, DateTime SubmittedAt,
    AnalysisState AnalysisStatus, string? Transcript, LearningFeedback? Feedback, string? Error, int RetryCount);
public record PracticeHistory(List<AttemptSummary> Attempts, int UsedToday, int DailyLimit);
