using DAL;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5214");

// הפרויקט פועל כרגע בסביבת פיתוח מקומית.
// עדיין אין מערכת הרשאות מלאה לפעולות הניהול ב-API.
// לא חושפים את פעולות הניהול באינטרנט לפני מימוש שכבת ההרשאות.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("This project runs in Development only.");
var serverConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(serverConnection))
    throw new InvalidOperationException("Set ConnectionStrings:DefaultConnection in API User Secrets. See README.");

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(new UserDB(DatabaseInitializer.GetDatabaseConnectionString(serverConnection)));

var app = builder.Build();
app.UseExceptionHandler();

// האתחול רץ לפני קבלת הבקשה הראשונה. כישלון חיבור עוצר את ההרצה.
bool seed = builder.Configuration.GetValue<bool>("Seed:Enabled");
var demoPassword = seed ? builder.Configuration["Seed:DemoPassword"] : null;
if (seed && string.IsNullOrEmpty(demoPassword))
    throw new InvalidOperationException("Set Seed:DemoPassword in API User Secrets, or set Seed:Enabled=false.");

await DatabaseInitializer.InitializeAsync(serverConnection, demoPassword);
app.MapOpenApi();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    status = "ready",
    project = "SpeakReady",
    stage = "V7"
})); app.Run();

