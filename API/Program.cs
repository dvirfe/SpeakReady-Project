using DAL;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// V1-V4 הוא שלב מקומי: עדיין אין התחברות עם session והרשאות ניהול.
// לא חושפים את פעולות הניהול באינטרנט לפני מימוש שכבת ההרשאות.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("This V1-V4 learning build runs in Development only.");

var serverConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(serverConnection))
    throw new InvalidOperationException("Set ConnectionStrings:DefaultConnection in API User Secrets. See README.");

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(new UserDB(DatabaseInitializer.GetDatabaseConnectionString(serverConnection)));

var app = builder.Build();
// מניעת חשיפה מקרית לרשת בשלב הבדיקות המקומי.
app.Use(async (context, next) =>
{
    var address = context.Connection.RemoteIpAddress;
    if (address is null || !System.Net.IPAddress.IsLoopback(address))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
app.UseExceptionHandler();

// האתחול רץ לפני קבלת הבקשה הראשונה. כישלון חיבור עוצר את ההרצה.
bool seed = builder.Configuration.GetValue<bool>("Seed:Enabled");
var demoPassword = seed ? builder.Configuration["Seed:DemoPassword"] : null;
if (seed && string.IsNullOrEmpty(demoPassword))
    throw new InvalidOperationException("Set Seed:DemoPassword in API User Secrets, or set Seed:Enabled=false.");

await DatabaseInitializer.InitializeAsync(serverConnection, demoPassword);
app.MapOpenApi();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ready", project = "SpeakReady", stage = "V1-V4" }));
app.Run();

