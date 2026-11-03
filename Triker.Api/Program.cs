using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Triker.Api.Data;
using Triker.Api.Hubs;
using Triker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR()
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

try
{
    var persistenceSvc = app.Services.GetRequiredService<GamePersistenceService>();
    await persistenceSvc.EnsureSchemaAsync();
    await app.Services.GetRequiredService<GameService>().LoadFromPersistenceAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "No se pudo inicializar la persistencia (PostgreSQL no disponible). La API arranca sin persistencia.");
}

app.UseCors();
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
    });

app.MapControllers();
app.MapHub<GameHub>("/gamehub");

app.Run();
