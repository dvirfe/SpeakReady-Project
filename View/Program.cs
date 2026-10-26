using View.Components;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// התצוגה תשתמש ב-HTTP בשלב הבא. אין הפניה ל-DAL ואין חיבור למסד כאן.
builder.Services.AddHttpClient("SpeakReadyAPI", client =>
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5214"));
string apiBaseUrl = builder.Configuration["ApiBaseUrl"]
?? throw new InvalidOperationException(
    "ApiBaseUrl not found in appsettings.json.");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl)
});
builder.Services.AddScoped<View.Services.UserSession>();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
