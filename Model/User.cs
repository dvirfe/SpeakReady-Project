using System.Text.Json.Serialization;

namespace Model;

public class User : BaseEntity
{
    // Username נוסף לתכנון כדי להתאים לבדיקות V3. FullName אינו חייב להיות ייחודי.
    public string Username { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    [JsonIgnore] public string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public string RoleName => Role.ToString();
    public TeacherRequestState TeacherRequestStatus { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public User() : base()
    {
    }
    // משתמש חדש מקבל תפקיד תלמיד. המזהה נקבע רק בעת השמירה
    public User(string username, string fullName, string email, string passwordHash)
        : this(0, username, fullName, email, passwordHash, UserRole.Student,
            TeacherRequestState.None, true, DateTime.UtcNow) { }

    // בנאי המשמש להמרת רשומה מבסיס הנתונים לאובייקט.
    public User(int id, string username, string fullName, string email, string passwordHash,
        UserRole role, TeacherRequestState teacherRequestStatus, bool isActive, DateTime createdAt)
        : base(id)
    {
        Username = username; FullName = fullName; Email = email; PasswordHash = passwordHash;
        Role = role; TeacherRequestStatus = teacherRequestStatus;
        IsActive = isActive; CreatedAt = createdAt;
    }

    public override string ToString() => $"{FullName} ({Username}) - {RoleName}";
}

