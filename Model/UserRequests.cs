using System.ComponentModel.DataAnnotations;

namespace Model;

// DTOs מגדירים במפורש אילו שדות מותר לקבל בכל בקשה.
public class CreateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9_.-]+$")]
    public string Username { get; set; } = "";
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = "";
}
public class LoginRequest
{
    [Required, StringLength(254)] public string Username { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}
public class UpdateEmailRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
public class UpdatePasswordRequest
{
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = "";
}
public class UpdateRoleRequest
{
    [EnumDataType(typeof(UserRole))] public UserRole Role { get; set; }
}
public class UpdateActiveRequest { public bool IsActive { get; set; } }
public record RoleCount(string RoleName, int Count);

