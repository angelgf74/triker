using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Triker.Api.Data;
using Triker.Api.Services;
using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Tests;

// Ciclo de vida de una partida: identidad por token, resultados, abandonar la
// mesa, concurrencia y restauracion tras un reinicio. Cada prueba corresponde
// a un fallo visto en la auditoria; si la regla se rompe, falla aqui.
public class GameServiceTests
{
    // Sin PostgreSQL: los guardados fallan dentro de GamePersistenceService,
    // que lo registra y sigue, igual que la API cuando arranca sin base.
    private sealed class SinBase : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => throw new InvalidOperationException("sin base en las pruebas");
    }

    private static GameService Nuevo()
    {
        var scoring = new ScoringService();
        return new GameService(new DeckService(), scoring, new BotService(scoring),
            new GamePersistenceService(new SinBase(), NullLogger<GamePersistenceService>.Instance),
            NullLogger<GameService>.Instance);
    }

    private static (GameService svc, GameState game, PlayerState ana, PlayerState berto) Mesa(int bots = 0)
    {
        var svc = Nuevo();
        var (game, ana) = svc.CreateGame("mesa", "Ana")!.Value;
        var (_, _, berto) = svc.JoinGame(game.Id, "Berto");
        for (int i = 0; i < bots; i++) Assert.NotNull(svc.AddBot(game.Id, ana.Id));
        return (svc, game, ana, berto!);
    }

    // Todos pasan sin descartar y se revela.
    private static void JugarRonda(GameService svc, GameState game)
    {
        while (game.Status == GameStatus.Playing)
            Assert.True(svc.SubmitDiscard(game.Id, game.CurrentTurnPlayer!.Id, []));
        Assert.NotNull(svc.GetRevealedHands(game.Id));
        Assert.Equal(GameStatus.Finished, game.Status);
    }

    private static (GameService svc, GameState game, PlayerState ana, PlayerState berto) RondaTerminada(int bots = 0)
    {
        var m = Mesa(bots);
        Assert.True(m.svc.StartGame(m.game.Id, m.ana.Id));
        JugarRonda(m.svc, m.game);
        return m;
    }

    // ── Identidad ───────────────────────────────────────────────────────────

    [Fact]
    public void Solo_el_token_del_asiento_identifica_al_jugador()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        var bot = game.Players.Single(p => p.IsBot);

        Assert.True(svc.IsPlayer(game, ana.Id, ana.Token));
        Assert.False(svc.IsPlayer(game, ana.Id, null));
        Assert.False(svc.IsPlayer(game, ana.Id, ""));
        Assert.False(svc.IsPlayer(game, ana.Id, "inventado"));
        Assert.False(svc.IsPlayer(game, ana.Id, berto.Token));      // el de otro asiento no vale
        Assert.False(svc.IsPlayer(game, bot.Id, null));              // nadie actua por un bot
        Assert.False(svc.IsPlayer(game, Guid.NewGuid(), ana.Token)); // ni en un asiento que no existe
    }

    [Fact]
    public void Cada_jugador_recibe_un_token_propio_y_largo()
    {
        var (_, _, ana, berto) = Mesa();

        Assert.NotEqual(ana.Token, berto.Token);
        Assert.True(ana.Token!.Length >= 40, $"token de {ana.Token.Length} caracteres");
    }

    // El id del jugador sale en el estado que reciben todos; el token, jamas.
    [Fact]
    public void Ningun_estado_enviado_lleva_tokens()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 1);
        var (_, _, espectador) = svc.AddSpectator(game.Id, "Mirón", null);
        svc.ConnectSpectator(game, espectador!.Id, espectador.Token, "conn-espectador");

        var vistas = new object[]
        {
            svc.ToDto(game, ana.Id),
            svc.ToDto(game, berto.Id),
            svc.ToDto(game, forPlayerId: null, isSpectator: true),
            svc.ListGames(),
        };
        foreach (var vista in vistas)
        {
            var json = JsonSerializer.Serialize(vista);
            Assert.DoesNotContain(ana.Token!, json);
            Assert.DoesNotContain(berto.Token!, json);
            Assert.DoesNotContain(espectador.Token!, json);
        }
    }

    [Fact]
    public void Conectarse_al_hub_exige_el_token()
    {
        var (svc, game, ana, _) = Mesa();

        Assert.False(svc.ConnectPlayer(game, ana.Id, "inventado", "conn-intruso"));
        Assert.Null(ana.ConnectionId);   // el intruso no le quita la conexion

        Assert.True(svc.ConnectPlayer(game, ana.Id, ana.Token, "conn-ana"));
        Assert.Equal("conn-ana", ana.ConnectionId);
    }

    [Fact]
    public void El_codigo_privado_tiene_seis_cifras()
    {
        var svc = Nuevo();
        for (int i = 0; i < 50; i++)
        {
            var (game, _) = svc.CreateGame($"p{i}", "Ana", isPrivate: true)!.Value;
            Assert.Matches("^[0-9]{6}$", game.AccessCode);
        }
    }

    // ── Espectadores ────────────────────────────────────────────────────────

    // Antes se borraba al perder la conexion y la reconexion automatica volvia
    // con un id que ya no existia: no recibia nada mas.
    [Fact]
    public void Un_espectador_que_pierde_la_conexion_puede_volver()
    {
        var (svc, game, _, _) = Mesa();
        var (_, _, s) = svc.AddSpectator(game.Id, "Mirón", null);

        Assert.True(svc.ConnectSpectator(game, s!.Id, s.Token, "conn-1"));
        Assert.Single(svc.ToDto(game, forPlayerId: null, isSpectator: true).Spectators);

        Assert.Equal(game.Id, svc.MarkSpectatorDisconnected("conn-1"));
        Assert.Empty(svc.ToDto(game, forPlayerId: null, isSpectator: true).Spectators);

        Assert.True(svc.ConnectSpectator(game, s.Id, s.Token, "conn-2"));
        Assert.Single(svc.ToDto(game, forPlayerId: null, isSpectator: true).Spectators);
    }

    // La conexion vieja puede cerrarse en el servidor despues de que la nueva
    // ya se haya registrado: no debe desconectar al espectador.
    [Fact]
    public void Cerrar_una_conexion_vieja_no_desconecta_al_espectador()
    {
        var (svc, game, _, _) = Mesa();
        var (_, _, s) = svc.AddSpectator(game.Id, "Mirón", null);
        svc.ConnectSpectator(game, s!.Id, s.Token, "conn-1");
        svc.ConnectSpectator(game, s.Id, s.Token, "conn-2");

        Assert.Null(svc.MarkSpectatorDisconnected("conn-1"));
        Assert.Equal("conn-2", s.ConnectionId);
    }

    // ── Resultados ──────────────────────────────────────────────────────────

    [Fact]
    public void Los_resultados_van_en_el_estado_mientras_la_ronda_esta_terminada()
    {
        var (svc, game, ana, berto) = RondaTerminada();

        var resultados = svc.ToDto(game, ana.Id).Results;
        Assert.NotNull(resultados);
        Assert.Equal(new[] { ana.Id, berto.Id }.Order(), resultados!.Select(r => r.PlayerId).Order());

        svc.SetContinue(game.Id, ana.Id, true);
        svc.SetContinue(game.Id, berto.Id, true);

        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Null(svc.ToDto(game, ana.Id).Results);
    }

    [Fact]
    public void Revelar_dos_veces_no_reparte_dos_veces_las_medallas()
    {
        var (svc, game, _, _) = RondaTerminada(bots: 1);
        var medallas = game.Players.Sum(p => p.Wins + p.Seconds + p.Thirds);

        Assert.Null(svc.GetRevealedHands(game.Id));
        Assert.Equal(medallas, game.Players.Sum(p => p.Wins + p.Seconds + p.Thirds));
    }

    // ── Abandonar la mesa ───────────────────────────────────────────────────

    [Fact]
    public void Abandonar_saca_al_jugador_y_los_demas_no_le_esperan()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 1);

        Assert.True(svc.SetContinue(game.Id, berto.Id, false));
        Assert.DoesNotContain(game.Players, p => p.Id == berto.Id);

        Assert.True(svc.SetContinue(game.Id, ana.Id, true));
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(2, game.RoundNumber);
    }

    [Fact]
    public void Si_el_resto_ya_habia_decidido_seguir_la_ronda_arranca_al_irse_el_ultimo()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 1);

        svc.SetContinue(game.Id, ana.Id, true);
        Assert.Equal(GameStatus.Finished, game.Status);

        svc.SetContinue(game.Id, berto.Id, false);
        Assert.Equal(GameStatus.Playing, game.Status);
    }

    // Antes repartia una ronda en solitario que el que quedaba ganaba siempre.
    [Fact]
    public void Si_queda_un_solo_jugador_la_mesa_vuelve_a_la_sala_de_espera()
    {
        var (svc, game, ana, berto) = RondaTerminada();

        svc.SetContinue(game.Id, ana.Id, false); // se va la creadora

        Assert.Equal(GameStatus.Waiting, game.Status);
        Assert.Equal(berto.Id, Assert.Single(game.Players).Id);
        Assert.Equal(berto.Id, game.CreatorId);   // hereda el puesto
        Assert.Empty(berto.Hand);
        Assert.Null(svc.ToDto(game, berto.Id).Results);
        Assert.Equal(2, game.RoundNumber);        // la siguiente ronda es la 2
        Assert.NotNull(svc.AddBot(game.Id, berto.Id));
    }

    [Fact]
    public void Si_se_van_todos_los_humanos_la_partida_se_cierra()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 2);

        svc.SetContinue(game.Id, ana.Id, false);
        svc.SetContinue(game.Id, berto.Id, false);

        Assert.Null(svc.GetGame(game.Id));
    }

    // El temporizador de fin de ronda saca a todos los indecisos de una vez.
    // Uno a uno, al sacar al primero la mesa volvia a la sala de espera y el
    // segundo se quedaba dentro como jugador fantasma (ya habia recibido el
    // aviso de que le sacaban) en una mesa que nadie podia iniciar.
    [Fact]
    public void Al_agotarse_la_decision_salen_todos_los_indecisos_a_la_vez()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 1);

        var fuera = svc.RemoveUndecidedPlayers(game.Id);

        Assert.Equal(new[] { ana.Id, berto.Id }.Order(), fuera.Select(f => f.Id).Order());
        Assert.Null(svc.GetGame(game.Id)); // solo queda el bot: se cierra
    }

    [Fact]
    public void Al_agotarse_la_decision_sigue_quien_ya_habia_decidido()
    {
        var svc = Nuevo();
        var (game, ana) = svc.CreateGame("mesa", "Ana")!.Value;
        var berto = svc.JoinGame(game.Id, "Berto").player!;
        var carla = svc.JoinGame(game.Id, "Carla").player!;
        svc.StartGame(game.Id, ana.Id);
        JugarRonda(svc, game);
        svc.SetContinue(game.Id, carla.Id, true);

        var fuera = svc.RemoveUndecidedPlayers(game.Id);

        Assert.Equal(2, fuera.Count);
        Assert.Equal(carla.Id, Assert.Single(game.Players).Id);
        Assert.Equal(GameStatus.Waiting, game.Status); // sola: a la sala de espera
        Assert.Equal(carla.Id, game.CreatorId);
    }

    [Fact]
    public void Un_bot_no_puede_decidir_por_la_mesa()
    {
        var (svc, game, _, _) = RondaTerminada(bots: 1);
        var bot = game.Players.Single(p => p.IsBot);

        Assert.False(svc.SetContinue(game.Id, bot.Id, false));
        Assert.Contains(game.Players, p => p.Id == bot.Id);
    }

    // ── Salir de la mesa ────────────────────────────────────────────────────
    // Antes solo se podia salir con la ronda terminada. Quien queria irse de
    // la sala de espera o a mitad de ronda cerraba la pestaña: la sala quedaba
    // huerfana y, en juego, los demas pagaban 60 segundos por cada turno suyo.

    private static void PasarTodos(GameService svc, GameState game)
    {
        while (game.Status == GameStatus.Playing)
            Assert.True(svc.SubmitDiscard(game.Id, game.CurrentTurnPlayer!.Id, []));
    }

    [Fact]
    public void Salir_de_la_sala_de_espera_quita_al_jugador()
    {
        var (svc, game, ana, berto) = Mesa();

        Assert.True(svc.LeaveGame(game.Id, berto.Id));

        Assert.Equal(ana.Id, Assert.Single(game.Players).Id);
        Assert.False(svc.IsPlayer(game, berto.Id, berto.Token));
        Assert.Equal(GameStatus.Waiting, game.Status);
    }

    [Fact]
    public void Si_sale_el_creador_de_la_sala_de_espera_hereda_el_primer_humano()
    {
        var svc = Nuevo();
        var (game, ana) = svc.CreateGame("mesa", "Ana")!.Value;
        svc.AddBot(game.Id, ana.Id);                       // el bot va antes que Berto
        var berto = svc.JoinGame(game.Id, "Berto").player!;

        Assert.True(svc.LeaveGame(game.Id, ana.Id));

        Assert.Equal(berto.Id, game.CreatorId);
        Assert.NotNull(svc.AddBot(game.Id, berto.Id));     // puede seguir montando la mesa
    }

    [Fact]
    public void Si_sale_el_ultimo_humano_de_la_sala_de_espera_la_partida_se_cierra()
    {
        var svc = Nuevo();
        var (game, ana) = svc.CreateGame("mesa", "Ana")!.Value;
        svc.AddBot(game.Id, ana.Id);

        Assert.True(svc.LeaveGame(game.Id, ana.Id));

        Assert.Null(svc.GetGame(game.Id));
        Assert.Empty(svc.ListGames());
    }

    [Fact]
    public void Salir_en_tu_turno_pasa_en_el_acto_y_el_asiento_deja_de_valer()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        svc.StartGame(game.Id, ana.Id);
        Assert.Equal(ana.Id, game.CurrentTurnPlayer!.Id);

        Assert.True(svc.LeaveGame(game.Id, ana.Id));

        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(berto.Id, game.CurrentTurnPlayer!.Id);   // nadie espera su turno
        Assert.False(svc.IsPlayer(game, ana.Id, ana.Token));
        Assert.False(svc.ConnectPlayer(game, ana.Id, ana.Token, "conn-vuelve"));
    }

    // Sigue en la lista hasta revelar, pero ya no debe recibir el estado
    // (que para un jugador incluye su mano).
    [Fact]
    public void Quien_sale_a_mitad_de_ronda_deja_de_recibir_el_estado()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        svc.ConnectPlayer(game, ana.Id, ana.Token, "conn-ana");
        svc.ConnectPlayer(game, berto.Id, berto.Token, "conn-berto");
        svc.StartGame(game.Id, ana.Id);

        svc.LeaveGame(game.Id, berto.Id);

        Assert.Equal(["conn-ana"], svc.PersonalizedStates(game).Select(s => s.connectionId));
    }

    [Fact]
    public void El_turno_salta_a_quien_salio_antes_de_que_le_tocara()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);          // orden: Ana, Berto, bot
        var bot = game.Players.Single(p => p.IsBot);
        svc.StartGame(game.Id, ana.Id);

        Assert.True(svc.LeaveGame(game.Id, berto.Id));
        Assert.Equal(ana.Id, game.CurrentTurnPlayer!.Id);     // sigue siendo el turno de Ana

        Assert.True(svc.SubmitDiscard(game.Id, ana.Id, []));

        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(bot.Id, game.CurrentTurnPlayer!.Id);
    }

    [Fact]
    public void Si_quien_salio_era_el_ultimo_del_orden_la_ronda_se_revela_sin_esperarle()
    {
        var svc = Nuevo();
        var (game, ana) = svc.CreateGame("mesa", "Ana")!.Value;
        svc.AddBot(game.Id, ana.Id);
        var berto = svc.JoinGame(game.Id, "Berto").player!;   // orden: Ana, bot, Berto
        svc.StartGame(game.Id, ana.Id);
        svc.LeaveGame(game.Id, berto.Id);

        Assert.True(svc.SubmitDiscard(game.Id, ana.Id, []));
        Assert.True(svc.ProcessBotTurn(game.Id));

        Assert.Equal(GameStatus.Revealing, game.Status);
    }

    [Fact]
    public void La_mano_de_quien_salio_no_cuenta_al_revelar()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        svc.StartGame(game.Id, ana.Id);
        svc.LeaveGame(game.Id, berto.Id);
        PasarTodos(svc, game);

        var resultados = svc.GetRevealedHands(game.Id);

        Assert.NotNull(resultados);
        Assert.Equal(2, resultados.Count);
        Assert.DoesNotContain(resultados, r => r.PlayerId == berto.Id);
        Assert.DoesNotContain(game.Players, p => p.Id == berto.Id);
        Assert.Equal(GameStatus.Finished, game.Status);
    }

    [Fact]
    public void Si_el_creador_salio_en_juego_al_revelar_hereda_el_primer_humano()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        svc.StartGame(game.Id, ana.Id);
        svc.LeaveGame(game.Id, ana.Id);
        PasarTodos(svc, game);

        Assert.NotNull(svc.GetRevealedHands(game.Id));

        Assert.Equal(berto.Id, game.CreatorId);
    }

    // Dos jugadores y uno se va: el otro termina la ronda y, como ya no hay
    // con quien jugar, la mesa vuelve a la sala de espera al pedir otra.
    [Fact]
    public void Si_se_va_el_unico_rival_la_ronda_termina_y_la_mesa_vuelve_a_la_sala_de_espera()
    {
        var (svc, game, ana, berto) = Mesa();
        svc.StartGame(game.Id, ana.Id);

        svc.LeaveGame(game.Id, ana.Id);
        Assert.True(svc.SubmitDiscard(game.Id, berto.Id, []));
        Assert.Equal(berto.Id, Assert.Single(svc.GetRevealedHands(game.Id)!).PlayerId);

        Assert.True(svc.SetContinue(game.Id, berto.Id, true));
        Assert.Equal(GameStatus.Waiting, game.Status);
        Assert.Equal(berto.Id, game.CreatorId);
    }

    [Fact]
    public void Si_salen_todos_los_humanos_en_juego_la_partida_se_cierra()
    {
        var (svc, game, ana, berto) = Mesa(bots: 2);
        svc.StartGame(game.Id, ana.Id);

        Assert.True(svc.LeaveGame(game.Id, ana.Id));
        Assert.True(svc.LeaveGame(game.Id, berto.Id));

        Assert.Null(svc.GetGame(game.Id));
    }

    [Fact]
    public void Salir_con_la_ronda_terminada_es_dejar_la_mesa()
    {
        var (svc, game, ana, berto) = RondaTerminada(bots: 1);

        Assert.True(svc.LeaveGame(game.Id, berto.Id));
        Assert.DoesNotContain(game.Players, p => p.Id == berto.Id);

        Assert.True(svc.SetContinue(game.Id, ana.Id, true));
        Assert.Equal(GameStatus.Playing, game.Status);
    }

    [Fact]
    public void Ni_un_bot_ni_quien_ya_salio_pueden_salir()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        var bot = game.Players.Single(p => p.IsBot);
        svc.StartGame(game.Id, ana.Id);

        Assert.False(svc.LeaveGame(game.Id, bot.Id));
        Assert.False(svc.LeaveGame(game.Id, Guid.NewGuid()));
        Assert.True(svc.LeaveGame(game.Id, berto.Id));
        Assert.False(svc.LeaveGame(game.Id, berto.Id));
        Assert.Equal(3, game.Players.Count);   // sigue en la lista hasta revelar: el orden de turnos no se toca
    }

    [Fact]
    public void El_estado_dice_a_los_demas_quien_ha_salido()
    {
        var (svc, game, ana, berto) = Mesa(bots: 1);
        svc.StartGame(game.Id, ana.Id);
        svc.LeaveGame(game.Id, berto.Id);

        var estado = svc.ToDto(game, ana.Id);

        Assert.True(estado.Players.Single(p => p.Id == berto.Id).HasLeft);
        Assert.False(estado.Players.Single(p => p.Id == ana.Id).HasLeft);
    }

    // ── Concurrencia ────────────────────────────────────────────────────────

    // En la API real 4 de cada 40 mesas saltaban de la ronda 1 a la 3.
    [Fact]
    public async Task Dos_continuar_a_la_vez_reparten_una_sola_ronda()
    {
        for (int i = 0; i < 200; i++)
        {
            var (svc, game, ana, berto) = RondaTerminada();
            using var salida = new Barrier(2);

            await Task.WhenAll(
                Task.Run(() => { salida.SignalAndWait(); svc.SetContinue(game.Id, ana.Id, true); }),
                Task.Run(() => { salida.SignalAndWait(); svc.SetContinue(game.Id, berto.Id, true); }));

            Assert.Equal(GameStatus.Playing, game.Status);
            Assert.Equal(2, game.RoundNumber);
        }
    }

    [Fact]
    public async Task Muchas_uniones_a_la_vez_no_pasan_del_maximo_de_la_mesa()
    {
        for (int i = 0; i < 20; i++)
        {
            var svc = Nuevo();
            var (game, _) = svc.CreateGame("llena", "J0")!.Value;

            await Task.WhenAll(Enumerable.Range(1, 40)
                .Select(k => Task.Run(() => svc.JoinGame(game.Id, $"J{k}"))));

            Assert.Equal(game.MaxPlayers, game.Players.Count);
            Assert.Equal(GameStatus.Playing, game.Status); // al llenarse arranca sola
        }
    }

    // ── Limites ─────────────────────────────────────────────────────────────

    [Fact]
    public void No_se_abren_mas_partidas_que_el_maximo()
    {
        var svc = Nuevo();
        for (int i = 0; i < Limites.PartidasMaximas; i++)
            Assert.NotNull(svc.CreateGame($"p{i}", "Ana"));

        Assert.Null(svc.CreateGame("una mas", "Ana"));
    }

    [Fact]
    public void Una_partida_no_admite_espectadores_sin_fin()
    {
        var (svc, game, _, _) = Mesa();
        for (int i = 0; i < Limites.EspectadoresMaximos; i++)
            Assert.Equal(GameService.SpectateResult.Ok, svc.AddSpectator(game.Id, $"e{i}", null).result);

        Assert.Equal(GameService.SpectateResult.Full, svc.AddSpectator(game.Id, "uno mas", null).result);
    }

    [Fact]
    public void La_meta_de_victorias_queda_dentro_de_lo_razonable()
    {
        var svc = Nuevo();
        Assert.Equal(0, svc.CreateGame("a", "Ana", targetWins: -5)!.Value.game.TargetWins);
        Assert.Equal(Limites.MetaMaxima, svc.CreateGame("b", "Ana", targetWins: 1_000_000)!.Value.game.TargetWins);
    }

    // ── Restaurar tras un reinicio ──────────────────────────────────────────

    private static GameState Guardada(GameStatus status, TimeSpan hace, bool conToken = true)
    {
        var game = new GameState { Status = status, LastActivityAt = DateTimeOffset.UtcNow - hace };
        game.Players.Add(new PlayerState { Name = "Ana", Token = conToken ? "t-ana" : null, ConnectionId = "conn-vieja" });
        game.Players.Add(new PlayerState { Name = "Bot", IsBot = true });
        game.Spectators.Add(new SpectatorState { Name = "Mirón", Token = "t-s", ConnectionId = "conn-vieja" });
        game.TurnDeadline = DateTimeOffset.UtcNow;
        return game;
    }

    [Fact]
    public void Al_restaurar_se_conservan_las_mesas_vivas_tambien_entre_rondas()
    {
        var svc = Nuevo();
        var jugando = Guardada(GameStatus.Playing, TimeSpan.FromMinutes(5));
        var entreRondas = Guardada(GameStatus.Finished, TimeSpan.FromMinutes(5));
        var esperando = Guardada(GameStatus.Waiting, TimeSpan.FromMinutes(5));

        var (restauradas, descartadas) = svc.RestoreGames([jugando, entreRondas, esperando], DateTimeOffset.UtcNow);

        Assert.Equal(3, restauradas.Count);
        Assert.Empty(descartadas);
        Assert.Same(entreRondas, svc.GetGame(entreRondas.Id));
        // Las conexiones no sobreviven al reinicio; el temporizador lo rearma InactivityService.
        Assert.All(restauradas, g => Assert.All(g.Players, p => Assert.Null(p.ConnectionId)));
        Assert.All(restauradas, g => Assert.Empty(g.Spectators));
        Assert.All(restauradas, g => Assert.Null(g.TurnDeadline));
    }

    [Fact]
    public void Al_restaurar_se_descartan_las_caducadas_y_las_que_no_tienen_token()
    {
        var svc = Nuevo();
        var viejaJugando = Guardada(GameStatus.Playing, GameService.ActiveGameTtl + TimeSpan.FromMinutes(1));
        var viejaTerminada = Guardada(GameStatus.Finished, GameService.FinishedGameTtl + TimeSpan.FromMinutes(1));
        var sinToken = Guardada(GameStatus.Playing, TimeSpan.FromMinutes(1), conToken: false);

        var (restauradas, descartadas) = svc.RestoreGames([viejaJugando, viejaTerminada, sinToken], DateTimeOffset.UtcNow);

        Assert.Empty(restauradas);
        Assert.Equal(3, descartadas.Count);
        Assert.Null(svc.GetGame(sinToken.Id));
    }
}
