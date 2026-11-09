
using System.ComponentModel.DataAnnotations;

namespace Model;

// הנתונים הנדרשים ליצירת משתמש חדש במערכת.
public class CreateUserRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_.-]+$")]
    public string Username { get; set; } = "";

    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = "";

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

// הנתונים שהמשתמש שולח בעת התחברות.
public class LoginRequest
{
    [Required]
    [StringLength(254)]
    public string Username { get; set; } = "";

    [Required]
    [StringLength(128)]
    public string Password { get; set; } = "";
}

// הנתונים הנדרשים לשינוי כתובת הדוא"ל.
public class UpdateEmailRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = "";
}

// הנתונים הנדרשים לשינוי סיסמה.
public class UpdatePasswordRequest
{
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

// הנתונים הנדרשים לשינוי תפקיד המשתמש.
public class UpdateRoleRequest
{
    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; }
}

// הנתונים הנדרשים להפעלה או להשבתה של משתמש.
public class UpdateActiveRequest
{
    public bool IsActive { get; set; }
}

// מחלקה לשמירת מספר המשתמשים בכל תפקיד.
public record RoleCount(string RoleName, int Count);
