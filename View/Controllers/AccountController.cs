using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Model;

namespace View.Controllers;

public sealed class AccountController(IHttpClientFactory factory) : Controller
{
    [HttpPost("/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginRequest credentials)
    {
        if (!ModelState.IsValid) return Redirect("/login?error=invalid");
        try
        {
            using var response = await factory.CreateClient("SpeakReadyAPI").PostAsJsonAsync("api/users/login", credentials);
            if (!response.IsSuccessStatusCode) return Redirect(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "/login?error=invalid" : "/login?error=unavailable");
            var login = await response.Content.ReadFromJsonAsync<LoginResult>(AppJson.Options);
            if (login is null) return Redirect("/login?error=unavailable");
            var claims = new[] {
                new Claim(ClaimTypes.NameIdentifier, login.User.Id.ToString()), new Claim(ClaimTypes.Name, login.User.Username),
                new Claim(ClaimTypes.Role, login.User.Role.ToString()), new Claim(ClaimTypes.Email, login.User.Email),
                new Claim("full_name", login.User.FullName), new Claim("api_token", login.Token)
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = new DateTimeOffset(login.ExpiresAt, TimeSpan.Zero), AllowRefresh = false });
            return Redirect(login.User.Role == UserRole.Student ? "/student" : "/pilot-info");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { return Redirect("/login?error=unavailable"); }
    }
    [HttpPost("/account/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/logout");
            request.Headers.Authorization = new("Bearer", User.FindFirst("api_token")?.Value);
            using var response = await factory.CreateClient("SpeakReadyAPI").SendAsync(request);
            // If the API is down, retain the cookie so logout/revocation can be retried.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                return Redirect("/session-error");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Redirect("/session-error"); }
        await HttpContext.SignOutAsync();
        return Redirect("/");
    }
}
