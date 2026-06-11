namespace Triker.Api.Services;

public class GameCleanupService(GameService gameService, ILogger<GameCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            var removed = gameService.CleanupStaleGames();
            if (removed > 0)
                logger.LogInformation("Cleaned up {Count} stale game(s)", removed);
        }
    }
}
