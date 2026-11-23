using System.Security.Claims;
using API.Services;
using DAL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model;
using MySql.Data.MySqlClient;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "Student")]
[Route("api/practice")]
public sealed class PracticeController(PracticeDB db, PrivateAudioStore audio, IConfiguration configuration) : ControllerBase
{
    private int StudentId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private int DailyLimit => Math.Clamp(configuration.GetValue("Practice:DailyLimit", 5), 1, 100);
    [HttpGet("exercises")]
    public async Task<ActionResult<List<ExerciseSummary>>> Exercises() => Ok(await db.GetExercisesAsync());
    [HttpGet("history")]
    public async Task<ActionResult<PracticeHistory>> History() => Ok(await db.GetHistoryAsync(StudentId, DailyLimit));
    [HttpGet("attempts/{id:int}")]
    public async Task<ActionResult<AttemptSummary>> Attempt(int id) => await db.GetAttemptAsync(StudentId, id) is { } value ? Ok(value) : NotFound();

    [HttpPost("attempts")]
    [RequestSizeLimit(PrivateAudioStore.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = PrivateAudioStore.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Submit([FromForm] int exerciseId, [FromForm] string submissionKey,
        [FromForm] bool consent, IFormFile file, CancellationToken cancellationToken)
    {
        if (!consent) return BadRequest(new { message = "יש לאשר שליחת ההקלטה לניתוח AI." });
        if (!Guid.TryParse(submissionKey, out var key)) return BadRequest(new { message = "מזהה שליחה לא תקין." });
        submissionKey = key.ToString();
        if (await db.FindSubmissionAsync(StudentId, submissionKey) is { } existing) return Ok(await db.GetAttemptAsync(StudentId, existing));
        if (await db.GetExerciseAsync(exerciseId) is not { } exercise) return NotFound();
        // Do not accept recordings that cannot be processed with the current server configuration.
        if (string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]))
            return StatusCode(503, new { message = "שירות המשוב עדיין לא הוגדר. אפשר להקליט ולהאזין, אך השליחה אינה זמינה כרגע." });
        string? saved = null;
        try
        {
            var recording = await audio.SaveAsync(file, Math.Min(exercise.MaxSeconds, 120), cancellationToken);
            saved = recording.Key;
            int id = await db.SubmitAsync(StudentId, exerciseId, submissionKey, saved, recording.Seconds, DailyLimit);
            saved = null; // Database owns this file now.
            return CreatedAtAction(nameof(Attempt), new { id }, await db.GetAttemptAsync(StudentId, id));
        }
        catch (InvalidAudioException ex) { return BadRequest(new { message = ex.Message }); }
        catch (QuotaExceededException)
        {
            if (await db.FindSubmissionAsync(StudentId, submissionKey) is { } duplicate) return Ok(await db.GetAttemptAsync(StudentId, duplicate));
            return StatusCode(429, new { message = "מכסת הניתוחים היומית נוצלה. אפשר לחזור מחר (המכסה מתחדשת בחצות UTC)." });
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            if (await db.FindSubmissionAsync(StudentId, submissionKey) is { } duplicate) return Ok(await db.GetAttemptAsync(StudentId, duplicate));
            return Conflict(new { message = "מזהה השליחה כבר קיים. התחילו הקלטה חדשה." });
        }
        finally { if (saved is not null) audio.Delete(saved); }
    }
    [HttpPost("attempts/{id:int}/retry")]
    public async Task<IActionResult> Retry(int id)
    {
        if (await db.GetAttemptAsync(StudentId, id) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"])) return StatusCode(503);
        try { return await db.RetryAsync(StudentId, id, DailyLimit) ? Accepted() : Conflict(new { message = "ניתן לנסות שוב רק ניתוח שנכשל, עד פעמיים." }); }
        catch (QuotaExceededException) { return StatusCode(429, new { message = "מכסת הניתוחים היומית נוצלה." }); }
    }
    [HttpGet("attempts/{id:int}/audio")]
    public async Task<IActionResult> Audio(int id)
    {
        var key = await db.GetAudioPathAsync(StudentId, id);
        if (key is null || !System.IO.File.Exists(audio.Resolve(key))) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(audio.Resolve(key), PrivateAudioStore.ContentType(key), enableRangeProcessing: true);
    }
    [HttpDelete("attempts/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var key = await db.DeleteAsync(StudentId, id, audio.Delete);
        if (key is null) return NotFound();
        return NoContent();
    }
}
