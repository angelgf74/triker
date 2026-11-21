namespace Triker.Api.Services;

// Cada minuto y no cada cuarto de hora: las salas de espera sin nadie
// conectado dejan de listarse a los dos minutos (GameService.EmptyRoomGrace) y
// eso no lo dispara ninguna peticion; solo se ve si alguien pasa a mirar.
public class GameCleanupService(GameService gameService, GameBroadcaster broadcaster, GamePersistenceService persistence, ILogger<GameCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            try
            {
                var (removed, lobbyChanged) = gameService.CleanupStaleGames(DateTimeOffset.UtcNow);
                if (removed > 0)
                    logger.LogInformation("Cleaned up {Count} stale game(s)", removed);
                if (lobbyChanged)
                    await broadcaster.BroadcastLobbyUpdate();
                // Lo que el guardado recuerda de las partidas ya borradas.
                persistence.ForgetDeleted(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una pasada que falla no puede parar la limpieza para siempre.
                logger.LogError(ex, "Game cleanup failed");
            }
        }
    }
}
