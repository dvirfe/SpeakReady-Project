using Model;
namespace View.Services;
public static class DisplayFormatting
{
    public static DateTime ToIsraelTime(this DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"));
    public static string ToHebrew(this AnalysisState state) => state switch {
        AnalysisState.Pending => "ממתין לניתוח", AnalysisState.Processing => "בעיבוד", AnalysisState.Completed => "המשוב מוכן",
        AnalysisState.Failed => "הניתוח נכשל", AnalysisState.Unintelligible => "נדרשת הקלטה חדשה", _ => "ממתין"
    };
}
