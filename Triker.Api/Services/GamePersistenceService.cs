using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Triker.Api.Data;
using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Api.Services;

public class GamePersistenceService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<GamePersistenceService> logger)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SaveAsync(GameState game)
    {
        try
        {
            var json = JsonSerializer.Serialize(game, _json);
            await using var db = await dbFactory.CreateDbContextAsync();
            var existing = await db.GameSnapshots.FindAsync(game.Id);
            if (existing == null)
                db.GameSnapshots.Add(new GameSnapshot { Id = game.Id, StateJson = json, UpdatedAt = DateTimeOffset.UtcNow });
            else
            {
                existing.StateJson = json;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist game {GameId}", game.Id);
        }
    }

    public async Task DeleteAsync(Guid gameId)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.GameSnapshots.Where(x => x.Id == gameId).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete snapshot {GameId}", gameId);
        }
    }

    public async Task DeleteManyAsync(IEnumerable<Guid> gameIds)
    {
        var ids = gameIds.ToList();
        if (ids.Count == 0) return;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.GameSnapshots.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete {Count} snapshots", ids.Count);
        }
    }

    public async Task<List<GameState>> LoadAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var snapshots = await db.GameSnapshots.ToListAsync();
        var games = new List<GameState>();
        foreach (var snap in snapshots)
        {
            try
            {
                var game = JsonSerializer.Deserialize<GameState>(snap.StateJson, _json);
                if (game != null) games.Add(game);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping corrupt snapshot {Id}", snap.Id);
            }
        }
        return games;
    }

    public async Task EnsureSchemaAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
