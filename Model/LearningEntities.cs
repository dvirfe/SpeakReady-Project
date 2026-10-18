namespace Model;

// שמות המזהים בטבלאות הם מפורשים (LessonId וכו'), ובמחלקות הם Id מהבסיס.
public class Lesson : BaseEntity
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
    public bool IsDemo { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Exercise : BaseEntity
{
    public int LessonId { get; set; }
    public string PromptText { get; set; } = "";
    public string Instructions { get; set; } = "";
    public string? MediaUrl { get; set; }
    public int SortOrder { get; set; }
    public int MaxSeconds { get; set; } = 120;
    public bool IsActive { get; set; } = true;
}

public class StudyGroup : BaseEntity
{
    public string GroupName { get; set; } = "";
    public int TeacherId { get; set; }
    public string JoinCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

// לטבלת הקישור אין Id מלאכותי. הזוג GroupId + StudentId הוא המפתח.
public class GroupMember
{
    public int GroupId { get; set; }
    public int StudentId { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Assignment : BaseEntity
{
    public int GroupId { get; set; }
    public int ExerciseId { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime DueAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PracticeAttempt : BaseEntity
{
    public int StudentId { get; set; }
    public int ExerciseId { get; set; }
    public int? AssignmentId { get; set; }
    public string SubmissionKey { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public int DurationSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }
    public AnalysisState AnalysisStatus { get; set; }
    public string? Transcript { get; set; }
    public string? AIFeedback { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public string? AnalysisError { get; set; }
    public string? TeacherFeedback { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

