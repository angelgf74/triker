using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Triker.Api;
using Triker.Api.Data;
using Triker.Api.Hubs;
using Triker.Api.Services;
using Triker.Api.State;

var builder = WebApplication.CreateBuilder(args);

// Las peticiones legitimas caben de sobra (un descarte son 5 GUID); esto corta
// los cuerpos enormes antes de deserializarlos.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = Limites.CuerpoMaximoBytes);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = Limites.CuerpoMaximoBytes)
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<DeckService>();
builder.Services.AddSingleton<ScoringService>();
builder.Services.AddSingleton<BotService>();
builder.Services.AddSingleton<GamePersistenceService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddSingleton<GameBroadcaster>();
builder.Services.AddSingleton<InactivityService>();
builder.Services.AddHostedService<GameCleanupService>();

// En el servidor la API esta detras de nginx en la misma maquina, que pone la
// IP real en X-Forwarded-For. Por defecto solo se cree a un proxy de loopback,
// que es justo ese; desde fuera la cabecera se ignora.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddTrikerRateLimits();

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

IReadOnlyList<GameState> restored = [];
try
{
    var persistenceSvc = app.Services.GetRequiredService<GamePersistenceService>();
    await persistenceSvc.EnsureSchemaAsync();
    restored = await app.Services.GetRequiredService<GameService>().LoadFromPersistenceAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "No se pudo inicializar la persistencia (PostgreSQL no disponible). La API arranca sin persistencia.");
}

// Las partidas restauradas retoman temporizadores y bots donde los dejaron.
app.Services.GetRequiredService<InactivityService>().ResumeRestoredGames(restored);

app.UseForwardedHeaders();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthorization();

// Salud para srv (api.salud en srv.json): 200 si la API lee las partidas guardadas y 503 si no.
// Sin base la API arranca igual, en memoria, y su raíz da 404 (que srv da por bueno): sin esto,
// un despliegue que no puede guardar partidas pasaría la comprobación y no volvería atrás.
app.MapMethods("/salud", [HttpMethods.Get, HttpMethods.Head],
    async (IDbContextFactory<AppDbContext> fabrica, ILogger<Program> log, CancellationToken ct) =>
    {
        try
        {
            await using var db = await fabrica.CreateDbContextAsync(ct);
            await db.GameSnapshots.AsNoTracking().AnyAsync(ct);
            return Results.Ok();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Salud: no se pueden leer las partidas guardadas");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }).DisableRateLimiting();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();
