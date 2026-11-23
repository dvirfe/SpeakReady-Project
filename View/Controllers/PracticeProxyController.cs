using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace View.Controllers;

// Browser -> same-origin View (cookie + CSRF) -> API (per-user bearer token).
[Authorize(Roles = "Student")]
public sealed class PracticeProxyController(IHttpClientFactory factory) : Controller
{
    [HttpPost("/practice/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024 + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024 + 64 * 1024)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] int exerciseId, [FromForm] string submissionKey, [FromForm] bool consent)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024) return BadRequest(new { message = "הקלטה חסרה או גדולה מדי." });
        using var form = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream());
        stream.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(file.ContentType);
        form.Add(stream, "file", "recording" + Path.GetExtension(file.FileName));
        form.Add(new StringContent(exerciseId.ToString()), "exerciseId");
        form.Add(new StringContent(submissionKey), "submissionKey");
        form.Add(new StringContent(consent.ToString()), "consent");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/practice/attempts") { Content = form };
        request.Headers.Authorization = new("Bearer", User.FindFirst("api_token")!.Value);
        try
        {
            using var response = await factory.CreateClient("SpeakReadyAPI").SendAsync(request, HttpContext.RequestAborted);
            return new ContentResult { StatusCode = (int)response.StatusCode, ContentType = "application/json", Content = await response.Content.ReadAsStringAsync() };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { return StatusCode(503, new { message = "השליחה לא אושרה. נסו לשלוח שוב; לא ייווצר ניסיון כפול." }); }
    }
    [HttpGet("/practice/audio/{id:int}")]
    public async Task<IActionResult> Audio(int id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/practice/attempts/{id}/audio");
        request.Headers.Authorization = new("Bearer", User.FindFirst("api_token")!.Value);
        using var response = await factory.CreateClient("SpeakReadyAPI").SendAsync(request, HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);
        Response.Headers.CacheControl = "no-store";
        return File(await response.Content.ReadAsByteArrayAsync(), response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream", enableRangeProcessing: true);
    }
}
