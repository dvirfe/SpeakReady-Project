using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using API.Services;
using DAL;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using MySql.Data.MySqlClient;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace SpeakReady.Tests;

public sealed class PilotFactory : WebApplicationFactory<Program>
{
    public readonly string AudioDirectory = Path.Combine(Path.GetTempPath(), "speakready-tests-" + Guid.NewGuid());
    public readonly FakeSpeech Fake = new();
    public string Connection => Environment.GetEnvironmentVariable("SPEAKREADY_TEST_DB")
        ?? throw new InvalidOperationException("Set SPEAKREADY_TEST_DB to a disposable MySQL 8 database server. Tests create SpeakReadyDB.");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", Connection);
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("OpenAI:ApiKey", "test-key-never-sent");
        builder.UseSetting("Audio:Directory", AudioDirectory);
        builder.ConfigureServices(services => {
            services.RemoveAll<ISpeechFeedbackService>();
            services.AddSingleton<ISpeechFeedbackService>(Fake);
        });
    }
}
public sealed class FakeSpeech : ISpeechFeedbackService
{
    public bool Fail;
    public Task<string> TranscribeAsync(string path, CancellationToken token)
    {
        if (Fail) throw new HttpRequestException("Simulated provider outage");
        return Task.FromResult("I enjoy reading because I learn new things.");
    }
    public Task<LearningFeedback?> AnalyzeAsync(string prompt, string transcript, CancellationToken token)
        => Task.FromResult<LearningFeedback?>(new(["התשובה ברורה"], ["הוסיפו דוגמה"], [], "תנו דוגמה לספר שקראתם."));
}

public sealed class PilotTests(PilotFactory factory) : IClassFixture<PilotFactory>
{
    private async Task<HttpClient> Student()
    {
        factory.Fake.Fail = false;
        var client = factory.CreateClient();
        var name = "s" + Guid.NewGuid().ToString("N");
        using var registration = await client.PostAsJsonAsync("/api/users", new CreateUserRequest { Username = name, FullName = "Test Student", Email = name + "@example.com", Password = "Test-password-123" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/users/login", new LoginRequest { Username = name, Password = "Test-password-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<LoginResult>(AppJson.Options);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session!.Token);
        return client;
    }
    private static byte[] Wave(bool silent = false, int seconds = 1)
    {
        int samples = 16000 * seconds;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (int i = 0; i < samples; i++) writer.Write((short)(silent ? 0 : Math.Sin(2 * Math.PI * 440 * i / 16000) * 8000));
        return stream.ToArray();
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string? key = null, byte[]? bytes = null, bool consent = true)
    {
        var exercises = await client.GetFromJsonAsync<List<ExerciseSummary>>("/api/practice/exercises", AppJson.Options);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes ?? Wave()); content.Headers.ContentType = new("audio/wav");
        form.Add(content, "file", "answer.wav");
        form.Add(new StringContent(exercises![0].Id.ToString()), "exerciseId");
        form.Add(new StringContent(key ?? Guid.NewGuid().ToString()), "submissionKey");
        form.Add(new StringContent(consent.ToString()), "consent");
        return await client.PostAsync("/api/practice/attempts", form);
    }
    private static async Task<AttemptSummary> Read(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<AttemptSummary>(body, AppJson.Options)!;
    }
    private static async Task<AttemptSummary> Wait(HttpClient client, int id)
    {
        for (int i = 0; i < 40; i++)
        {
            var result = await client.GetFromJsonAsync<AttemptSummary>($"/api/practice/attempts/{id}", AppJson.Options);
            if (result!.AnalysisStatus is not (AnalysisState.Pending or AnalysisState.Processing)) return result;
            await Task.Delay(500);
        }
        throw new Exception("Analysis did not complete within 20s");
    }
    [Fact] public async Task AnonymousAndStudentCannotAdministerUsers()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users")).StatusCode);
        using var student = await Student();
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsJsonAsync("/api/users/1/role", new { role = "Admin" })).StatusCode);
    }
    [Fact] public async Task LogoutRevokesToken()
    {
        using var client = await Student();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/users/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }
    [Fact] public async Task ContentSeedIsIdempotentAndHistoryStartsEmpty()
    {
        using var client = await Student();
        var db = factory.Services.GetRequiredService<PracticeDB>();
        await db.SeedContentAsync(); await db.SeedContentAsync();
        Assert.Equal(3, (await client.GetFromJsonAsync<List<ExerciseSummary>>("/api/practice/exercises"))!.Count);
        var history = await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options);
        Assert.Empty(history!.Attempts); Assert.Equal(0, history.UsedToday);
    }
    [Fact] public async Task RecordingIsProcessedPrivateAndDeletable()
    {
        using var owner = await Student(); using var other = await Student();
        var attempt = await Read(await Upload(owner));
        var completed = await Wait(owner, attempt.Id);
        Assert.Equal(AnalysisState.Completed, completed.AnalysisStatus);
        Assert.NotNull(completed.Transcript); Assert.NotNull(completed.Feedback);
        var json = await owner.GetStringAsync($"/api/practice/attempts/{attempt.Id}");
        Assert.DoesNotContain("audioPath", json); Assert.DoesNotContain(factory.AudioDirectory, json);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/practice/attempts/{attempt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/practice/attempts/{attempt.Id}/audio")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/practice/attempts/{attempt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/practice/attempts/{attempt.Id}/retry", null)).StatusCode);
        Assert.Equal(Wave(), await owner.GetByteArrayAsync($"/api/practice/attempts/{attempt.Id}/audio"));
        var files = Directory.GetFiles(factory.AudioDirectory).Length;
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/practice/attempts/{attempt.Id}")).StatusCode);
        Assert.Equal(files - 1, Directory.GetFiles(factory.AudioDirectory).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/practice/attempts/{attempt.Id}/audio")).StatusCode);
        Assert.Equal(1, (await owner.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!.UsedToday);
    }
    [Fact] public async Task DuplicateConcurrentSubmissionIsOneAttemptAndOneCharge()
    {
        using var client = await Student();
        string key = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(Upload(client, key), Upload(client, key));
        var a = await Read(results[0]); var b = await Read(results[1]); Assert.Equal(a.Id, b.Id);
        var history = await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options);
        Assert.Single(history!.Attempts); Assert.Equal(1, history.UsedToday);
        await Wait(client, a.Id);
    }
    [Fact] public async Task ConsentAndRealAudioRequired()
    {
        using var client = await Student();
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, consent: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, bytes: Encoding.UTF8.GetBytes("not audio"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(client, bytes: Wave(seconds: 123))).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!.UsedToday);
    }
    [Fact] public async Task SilenceDoesNotGenerateFeedback()
    {
        using var client = await Student();
        var attempt = await Read(await Upload(client, bytes: Wave(silent: true)));
        var completed = await Wait(client, attempt.Id);
        Assert.Equal(AnalysisState.Unintelligible, completed.AnalysisStatus); Assert.Null(completed.Feedback);
    }
    [Fact] public async Task DailyQuotaIsAtomicAndDeletionDoesNotResetIt()
    {
        using var client = await Student();
        var responses = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => Upload(client)));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        var history = (await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!;
        Assert.Equal(5, history.UsedToday);
        foreach (var item in history.Attempts) { await Wait(client, item.Id); await client.DeleteAsync($"/api/practice/attempts/{item.Id}"); }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Upload(client)).StatusCode);
    }
    [Fact] public async Task FailedAnalysisCanRetryTwiceWithoutNewRecording()
    {
        using var client = await Student(); factory.Fake.Fail = true;
        try
        {
            var attempt = await Read(await Upload(client));
            for (int i = 0; i < 3; i++)
            {
                var failed = await Wait(client, attempt.Id);
                Assert.Equal(AnalysisState.Failed, failed.AnalysisStatus);
                Assert.Equal(i, failed.RetryCount);
                var response = await client.PostAsync($"/api/practice/attempts/{attempt.Id}/retry", null);
                Assert.Equal(i < 2 ? HttpStatusCode.Accepted : HttpStatusCode.Conflict, response.StatusCode);
            }
            Assert.Equal(3, (await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!.UsedToday);
        }
        finally { factory.Fake.Fail = false; }
    }
    [Fact] public async Task AdministrativeChangesRevokeSessions()
    {
        using var admin = await Student(); using var student = await Student();
        var adminUser = (await admin.GetFromJsonAsync<User>("/api/users/me", AppJson.Options))!;
        var studentUser = (await student.GetFromJsonAsync<User>("/api/users/me", AppJson.Options))!;
        await factory.Services.GetRequiredService<UserDB>().UpdateUserRoleAsync(adminUser.Id, UserRole.Admin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/users/me")).StatusCode);
        using var login = await admin.PostAsJsonAsync("/api/users/login", new LoginRequest { Username = adminUser.Username, Password = "Test-password-123" });
        var session = (await login.Content.ReadFromJsonAsync<LoginResult>(AppJson.Options))!;
        admin.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/users/{studentUser.Id}/active", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/practice/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.PostAsJsonAsync("/api/users/login", new LoginRequest { Username = studentUser.Username, Password = "Test-password-123" })).StatusCode);
    }
    [Fact] public async Task MissingProviderKeyRejectsUploadWithoutCharging()
    {
        using var client = await Student();
        using var noAiFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("OpenAI:ApiKey", ""));
        using var noAi = noAiFactory.CreateClient();
        noAi.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Upload(noAi)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!.UsedToday);
    }
    [Fact] public async Task ExpiredWorkLeaseCanBeReclaimed()
    {
        using var client = await Student();
        var attempt = await Read(await Upload(client));
        await Wait(client, attempt.Id);
        using var connection = new MySqlConnection(DatabaseInitializer.GetDatabaseConnectionString(factory.Connection));
        await connection.OpenAsync();
        using var command = new MySqlCommand("UPDATE PracticeAttempts p JOIN AnalysisJobs j ON p.AttemptId=j.AttemptId SET p.AnalysisStatus='Processing',j.LeaseUntil=DATE_SUB(UTC_TIMESTAMP(),INTERVAL 1 MINUTE) WHERE p.AttemptId=@id", connection);
        command.Parameters.AddWithValue("@id", attempt.Id); await command.ExecuteNonQueryAsync();
        Assert.Equal(AnalysisState.Completed, (await Wait(client, attempt.Id)).AnalysisStatus);
        Assert.Equal(1, (await client.GetFromJsonAsync<PracticeHistory>("/api/practice/history", AppJson.Options))!.UsedToday);
    }
    [Fact] public async Task ExpiredSessionIsRejected()
    {
        using var client = await Student();
        var user = (await client.GetFromJsonAsync<User>("/api/users/me", AppJson.Options))!;
        using var connection = new MySqlConnection(DatabaseInitializer.GetDatabaseConnectionString(factory.Connection));
        await connection.OpenAsync();
        using var command = new MySqlCommand("UPDATE AuthSessions SET ExpiresAt=DATE_SUB(UTC_TIMESTAMP(), INTERVAL 1 SECOND) WHERE UserId=@id", connection);
        command.Parameters.AddWithValue("@id", user.Id); await command.ExecuteNonQueryAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/practice/history")).StatusCode);
    }
}

public sealed class ValidationTests
{
    [Fact] public void Utf8PasswordLimitAndSharedRulesAreEnforced()
    {
        Assert.NotNull(UserValidation.Validate(new CreateUserRequest { Username = "ab", FullName = "Student", Email = "bad", Password = "short" }));
        Assert.NotNull(UserValidation.Validate(new CreateUserRequest { Username = "valid", FullName = "Student", Email = "valid@example.com", Password = new string('א', 40) }));
    }
    [Fact] public void InventedCorrectionsAreRejected()
    {
        Assert.Throws<JsonException>(() => OpenAiFeedbackService.ValidateFeedback(new(["clear"], ["example"], [new("invented quote", "better", "why")], "next"), "I like reading."));
        OpenAiFeedbackService.ValidateFeedback(new(["clear"], ["example"], [new("I like reading", "I enjoy reading", "vocabulary")], "next"), "I like reading.");
    }
}
