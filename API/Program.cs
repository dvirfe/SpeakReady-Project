using DAL;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);
// הגבלת הרצת השרת למחשב המקומי.
builder.WebHost.UseUrls("http://localhost:5214");
// בדיקה שהפרויקט פועל בסביבת פיתוח.
if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "This project runs in Development only.");
}
// קבלת מחרוזת החיבור ל-MySQL מתוך הגדרות הפרויקט.
string? serverConnection = builder.Configuration.GetConnectionString(
    "DefaultConnection");
if (string.IsNullOrWhiteSpace(serverConnection))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings:DefaultConnection in API User Secrets.");
}
// הגדרת השירותים שבהם משתמש ה-API.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// יצירת השירות שמבצע פעולות על טבלת המשתמשים.
builder.Services.AddSingleton(
    new UserDB(
        DatabaseInitializer.GetDatabaseConnectionString(
            serverConnection)));
var app = builder.Build();
app.UseExceptionHandler();
// בדיקה האם צריך ליצור משתמשי הדגמה.
bool seed = builder.Configuration.GetValue<bool>("Seed:Enabled");
string? demoPassword = null;
if (seed)
{
    demoPassword = builder.Configuration["Seed:DemoPassword"];
}
// בדיקה שהוגדרה סיסמה למשתמשי ההדגמה.
if (seed && string.IsNullOrEmpty(demoPassword))
{
    throw new InvalidOperationException(
        "Set Seed:DemoPassword in API User Secrets, or set Seed:Enabled=false.");
}
// יצירת בסיס הנתונים והטבלאות במידת הצורך.
await DatabaseInitializer.InitializeAsync(
    serverConnection,
    demoPassword);
// הגדרת הנתיבים של ה-API.
app.MapOpenApi();
app.MapControllers();
// נתיב לבדיקה שהשרת פועל.
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ready",
        project = "SpeakReady",
        stage = "V7"
    });
});
// הפעלת השרת.
app.Run();
