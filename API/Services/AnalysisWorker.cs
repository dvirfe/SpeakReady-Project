using DAL;
using Model;

namespace API.Services;

public sealed class AnalysisWorker(PracticeDB db, PrivateAudioStore audio, IServiceScopeFactory scopes,
    ILogger<AnalysisWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                var work = await db.ClaimAsync();
                if (work is null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                var started = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromMinutes(3)); // Shorter than the persistent job lease.
                    using var scope = scopes.CreateScope();
                    var ai = scope.ServiceProvider.GetRequiredService<ISpeechFeedbackService>();
                    if (!await audio.HasAudioEnergyAsync(work.AudioPath, timeout.Token))
                    {
                        await db.CompleteAsync(work.Id, AnalysisState.Unintelligible, null, null, "לא זוהה קול ברור. נסו להקליט שוב קרוב למיקרופון.");
                        continue;
                    }
                    var transcript = await ai.TranscribeAsync(audio.Resolve(work.AudioPath), timeout.Token);
                    var feedback = string.IsNullOrWhiteSpace(transcript) ? null : await ai.AnalyzeAsync(work.PromptText, transcript, timeout.Token);
                    await db.CompleteAsync(work.Id, feedback is null ? AnalysisState.Unintelligible : AnalysisState.Completed, transcript, feedback,
                        feedback is null ? "לא זוהתה תשובה ברורה באנגלית. נסו להקליט שוב במקום שקט." : null);
                    logger.LogInformation("Analysis {AttemptId} finished in {ElapsedMs} ms", work.Id, started.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    // Never log provider payloads, transcripts, credentials or file contents.
                    logger.LogWarning("Analysis {AttemptId} failed ({ErrorType})", work.Id, ex.GetType().Name);
                    await db.CompleteAsync(work.Id, AnalysisState.Failed, null, null, "הניתוח לא הושלם. אפשר לנסות שוב ללא הקלטה חדשה.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("Analysis queue unavailable ({ErrorType})", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
