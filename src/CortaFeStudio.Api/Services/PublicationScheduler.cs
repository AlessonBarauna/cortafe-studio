namespace CortaFeStudio.Api.Services;

public sealed class PublicationScheduler(SocialService social, ILogger<PublicationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            foreach (var publication in social.Due())
            {
                try { await social.ExecuteAsync(publication); }
                catch (Exception ex) { logger.LogError(ex, "Falha ao executar publicação {PublicationId}", publication.Id); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
