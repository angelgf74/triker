using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Triker.Api;

// Limites de peticiones por IP. Sin ellos se probaban 300 codigos privados en
// medio segundo y se creaban 300 partidas en otro tanto.
//
// La IP es la del cliente solo si UseForwardedHeaders va antes (Program.cs):
// en el servidor la API esta detras de nginx y sin eso todas las peticiones
// llegan de 127.0.0.1, con lo que el limite seria uno para todos los jugadores.
public static class RateLimits
{
    public const string Crear = "crear";
    public const string Entrar = "entrar";

    // Holgados para una clase o un bar detras de una sola IP; apretados para
    // un script. Con 30 intentos por minuto, el millon de codigos de seis
    // cifras lleva semanas.
    private const int CrearPorMinuto = 10;
    private const int EntrarPorMinuto = 30;
    private const int TotalPorMinuto = 600;

    public static IServiceCollection AddTrikerRateLimits(this IServiceCollection services)
        => services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
                await ctx.HttpContext.Response.WriteAsync("Demasiadas peticiones. Espera un minuto y vuelve a intentarlo.", ct);

            o.AddPolicy(Crear, ctx => PorIp(ctx, CrearPorMinuto));
            o.AddPolicy(Entrar, ctx => PorIp(ctx, EntrarPorMinuto));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => PorIp(ctx, TotalPorMinuto));
        });

    private static RateLimitPartition<string> PorIp(HttpContext ctx, int porMinuto)
        => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1) });
}
