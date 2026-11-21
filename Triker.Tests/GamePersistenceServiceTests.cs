using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Triker.Api.Data;
using Triker.Api.Services;

namespace Triker.Tests;

// Lo que el servicio de guardado recuerda de cada partida. Al borrar una, se
// queda una marca para que un guardado rezagado no la resucite; esa marca no
// se quitaba nunca: una entrada mas por cada partida cerrada, para siempre.
public class GamePersistenceServiceTests
{
    // Sin PostgreSQL, como en GameServiceTests: guardar y borrar fallan por
    // dentro, se registra y se sigue.
    private sealed class SinBase : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("sin base en las pruebas");
    }

    private static GamePersistenceService Nuevo()
        => new(new SinBase(), NullLogger<GamePersistenceService>.Instance);

    [Fact]
    public async Task La_marca_de_una_partida_borrada_se_olvida_pasado_un_rato()
    {
        var svc = Nuevo();
        var ahora = DateTimeOffset.UtcNow;
        await svc.DeleteManyAsync([Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);
        Assert.Equal(3, svc.TrackedCount);

        // Recien borradas se conservan: todavia puede llegar un guardado rezagado.
        Assert.Equal(0, svc.ForgetDeleted(ahora + TimeSpan.FromMinutes(1)));
        Assert.Equal(3, svc.TrackedCount);

        Assert.Equal(3, svc.ForgetDeleted(ahora + GamePersistenceService.ForgetDeletedAfter + TimeSpan.FromMinutes(1)));
        Assert.Equal(0, svc.TrackedCount);
    }

    [Fact]
    public async Task Olvidar_no_toca_las_partidas_que_siguen_vivas()
    {
        var svc = Nuevo();
        var viva = new Triker.Api.State.GameState();
        await svc.SaveAsync(viva);              // falla al escribir (sin base), pero la partida queda apuntada
        await svc.DeleteAsync(Guid.NewGuid());
        var antes = svc.TrackedCount;

        var olvidadas = svc.ForgetDeleted(DateTimeOffset.UtcNow + TimeSpan.FromHours(1));

        Assert.Equal(1, olvidadas);
        Assert.Equal(antes - 1, svc.TrackedCount);
    }
}
