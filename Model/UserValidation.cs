using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Model;

// Shared by the browser form and API DTO validation.
public static class UserValidation
{
    public static string? Validate(CreateUserRequest request)
    {
        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(request, new ValidationContext(request), errors, true)
            ? null : string.Join(" ", errors.Select(e => e.ErrorMessage));
    }
}

public sealed class PasswordBytesAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not string password || Encoding.UTF8.GetByteCount(password) <= 72;
    public PasswordBytesAttribute() => ErrorMessage = "הסיסמה ארוכה מדי; השתמשו בעד 72 בתים בתווי UTF-8.";
}
