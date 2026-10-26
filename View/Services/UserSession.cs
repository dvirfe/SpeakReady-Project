
using Model;

namespace View.Services;

public class UserSession
{
    public int UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }

    public bool IsLoggedIn => UserId > 0;

    public event Action? OnChange;

    public void Login(User user)
    {
        UserId = user.Id;
        Username = user.Username;
        FullName = user.FullName;
        Email = user.Email;
        Role = user.Role;

        if (OnChange != null)
        {
            OnChange.Invoke();
        }
    }

    public void Logout()
    {
        UserId = 0;
        Username = string.Empty;
        FullName = string.Empty;
        Email = string.Empty;
        Role = default;

        if (OnChange != null)
        {
            OnChange.Invoke();
        }
    }
}
