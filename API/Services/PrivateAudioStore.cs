using System.Diagnostics;
using System.Text.Json;

namespace API.Services;

public sealed class InvalidAudioException(string message) : Exception(message);
public sealed class PrivateAudioStore
{
    public const long MaxBytes = 10 * 1024 * 1024;
    private readonly string root;
    public PrivateAudioStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        root = Path.GetFullPath(configuration["Audio:Directory"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "audio"));
        Directory.CreateDirectory(root);
        if (environment.WebRootPath is { } webRoot && root.StartsWith(Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Audio storage must be outside the public web root.");
    }
    public string Resolve(string key)
    {
        if (Path.GetFileName(key) != key || string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Invalid storage key.");
        return Path.Combine(root, key);
    }
    public async Task<(string Key, int Seconds)> SaveAsync(IFormFile file, int maxSeconds, CancellationToken cancellationToken)
    {
        if (file.Length is <= 0 or > MaxBytes) throw new InvalidAudioException("יש לשלוח הקלטה עד 10MB.");
        string extension = file.ContentType.Split(';')[0].Trim().ToLowerInvariant() switch {
            "audio/webm" => ".webm", "audio/ogg" => ".ogg", "audio/mp4" => ".mp4", "audio/wav" => ".wav",
            _ => throw new InvalidAudioException("פורמט ההקלטה אינו נתמך.")
        };
        string key = Guid.NewGuid().ToString("N") + extension;
        string path = Resolve(key);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.CopyToAsync(output, cancellationToken);
            // Decode/measure actual audio, not a client-supplied duration or MIME label.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var start = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-f", Demuxer(key), "-select_streams", "a:0", "-show_entries", "stream=codec_type:frame=best_effort_timestamp_time,pkt_duration_time", "-show_frames", "-of", "json", path })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("ffprobe unavailable.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            string json = await stdout;
            await stderr;
            if (process.ExitCode != 0) throw new InvalidAudioException("הקובץ אינו הקלטת שמע תקינה.");
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("frames", out var frames) || frames.GetArrayLength() == 0)
                throw new InvalidAudioException("לא נמצאה הקלטת שמע בקובץ.");
            double seconds = 0;
            double lastEnd = 0;
            foreach (var frame in frames.EnumerateArray())
            {
                double duration = frame.TryGetProperty("pkt_duration_time", out var d) && double.TryParse(d.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
                seconds += duration;
                if (frame.TryGetProperty("best_effort_timestamp_time", out var t) && double.TryParse(t.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var timestamp))
                    lastEnd = Math.Max(lastEnd, timestamp + duration);
            }
            seconds = Math.Max(seconds, lastEnd);
            if (!double.IsFinite(seconds) || seconds < .5 || seconds > maxSeconds + 1)
                throw new InvalidAudioException($"אורך ההקלטה חייב להיות בין שנייה ל־{maxSeconds} שניות.");
            return (key, Math.Max(1, (int)Math.Ceiling(seconds)));
        }
        catch { Delete(key); throw; }
    }
    private static string Demuxer(string key) => Path.GetExtension(key) switch {
        ".webm" => "matroska", ".mp4" => "mov", ".ogg" => "ogg", ".wav" => "wav", _ => throw new InvalidAudioException("פורמט לא נתמך.")
    };
    public async Task<bool> HasAudioEnergyAsync(string key, CancellationToken token)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-protocol_whitelist", "file,pipe", "-f", Demuxer(key), "-i", Resolve(key), "-vn", "-af", "volumedetect", "-f", "null", "-" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg unavailable.");
        var stderr = process.StandardError.ReadToEndAsync(token);
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        string output = await stderr;
        await stdout;
        if (process.ExitCode != 0) throw new InvalidAudioException("ההקלטה אינה ניתנת לפענוח.");
        var match = System.Text.RegularExpressions.Regex.Match(output, @"max_volume: ([-\d.]+) dB");
        return match.Success && double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > -50;
    }
    public void Delete(string key) => File.Delete(Resolve(key));
    public static string ContentType(string key) => Path.GetExtension(key) switch {
        ".webm" => "audio/webm", ".ogg" => "audio/ogg", ".mp4" => "audio/mp4", ".wav" => "audio/wav", _ => "application/octet-stream"
    };
}
