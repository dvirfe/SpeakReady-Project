using Microsoft.AspNetCore.Components.Authorization;
using Model;

namespace View.Services;

// A UI projection only. Every API operation independently validates the session and owner.
public sealed class UserSession(AuthenticationStateProvider authentication)
{
    public int UserId { get; private set; }
    public string Username { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public UserRole Role { get; private set; }
    public bool IsLoggedIn => UserId > 0;
    public event Action? OnChange;
    public async Task InitializeAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        UserId = int.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
        Username = user.Identity?.Name ?? "";
        FullName = user.FindFirst("full_name")?.Value ?? "";
        Email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
        Role = Enum.TryParse<UserRole>(user.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var role) ? role : default;
        OnChange?.Invoke();
    }
}
