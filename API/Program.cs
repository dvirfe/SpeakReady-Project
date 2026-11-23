using API.Authentication;
using API.Services;
using DAL;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
string connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Set ConnectionStrings:DefaultConnection in API User Secrets or environment variables.");
string databaseConnection = DatabaseInitializer.GetDatabaseConnectionString(connection);
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(new UserDB(databaseConnection));
builder.Services.AddSingleton(sp => new SessionDB(databaseConnection, sp.GetRequiredService<UserDB>()));
builder.Services.AddSingleton(new PracticeDB(databaseConnection));
builder.Services.AddSingleton<PrivateAudioStore>();
builder.Services.AddAuthentication("Session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("Session", null);
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("accounts", limit => { limit.PermitLimit = 60; limit.Window = TimeSpan.FromMinutes(1); limit.QueueLimit = 0; });
});
builder.Services.AddHttpClient<ISpeechFeedbackService, OpenAiFeedbackService>(http => {
    http.BaseAddress = new Uri("https://api.openai.com/v1/");
    http.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHostedService<AnalysisWorker>();
var app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
bool seed = app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Seed:Enabled");
string? demoPassword = seed ? builder.Configuration["Seed:DemoPassword"] : null;
if (seed && string.IsNullOrEmpty(demoPassword)) throw new InvalidOperationException("Set Seed:DemoPassword or disable Seed:Enabled.");
app.Logger.LogInformation("Initializing database schema");
await DatabaseInitializer.InitializeAsync(connection, demoPassword);
await app.Services.GetRequiredService<PracticeDB>().SeedContentAsync();
app.Logger.LogInformation("Database and pilot content ready");
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ready", project = "SpeakReady" }));
app.Run();

public partial class Program { }
