using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Triker.Api.Data;
using Triker.Api.State;

namespace Triker.Api.Services;

// Copia de cada partida en PostgreSQL. GameService pide los guardados sin
// esperarlos, y antes dos podian terminar en cualquier orden: el snapshot
// viejo pisaba al nuevo, y los dos primeros chocaban al insertar la misma
// clave. Ahora:
//   - SaveAsync serializa en el acto (quien llama tiene el cerrojo de la
//     partida, asi que el JSON es un estado coherente) y numera la version;
//   - las escrituras de una partida van de una en una y se salta la que
//     llega con una version mas vieja que la ultima escrita;
//   - DeleteAsync marca la partida para que un guardado rezagado no la resucite.
public class GamePersistenceService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<GamePersistenceService> logger)
{
    private static readonly JsonSerializerOptions _json = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const long Deleted = long.MaxValue;

    private long _version;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<Guid, long> _written = new();

    public Task SaveAsync(GameState game)
    {
        string json;
        try { json = JsonSerializer.Serialize(game, _json); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize game {GameId}", game.Id);
            return Task.CompletedTask;
        }
        return WriteAsync(game.Id, json, Interlocked.Increment(ref _version));
    }

    private async Task WriteAsync(Guid id, string json, long version)
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_written.TryGetValue(id, out var last) && last >= version) return;

            await using var db = await dbFactory.CreateDbContextAsync();
            var existing = await db.GameSnapshots.FindAsync(id);
            if (existing == null)
                db.GameSnapshots.Add(new Data.GameSnapshot { Id = id, StateJson = json, UpdatedAt = DateTimeOffset.UtcNow });
            else
            {
                existing.StateJson = json;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
            _written[id] = version;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist game {GameId}", id);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task DeleteAsync(Guid gameId) => DeleteManyAsync([gameId]);

    public async Task DeleteManyAsync(IEnumerable<Guid> gameIds)
    {
        var ids = gameIds.ToList();
        if (ids.Count == 0) return;

        // Primero se marca: toda escritura que empiece despues se salta. Luego
        // se espera a la que ya estuviera en marcha, y solo entonces se borra.
        foreach (var id in ids)
            _written[id] = Deleted;

        foreach (var id in ids)
        {
            if (!_gates.TryRemove(id, out var gate)) continue;
            await gate.WaitAsync();
            gate.Release();
        }

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
        var snapshots = await db.GameSnapshots.AsNoTracking().ToListAsync();
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
