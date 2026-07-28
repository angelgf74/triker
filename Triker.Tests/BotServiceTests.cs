using System.Reflection;
using Triker.Api.Services;
using Triker.Api.State;
using Triker.Shared.Models;

namespace Triker.Tests;

public class BotServiceTests
{
    private readonly BotService _bots = new(new ScoringService());

    // La mesa admite 16 jugadores; si el roster no da para llenarla, PickBot cae
    // al "Bot-799" de reserva, que es lo que motivó ampliarlo a 36 nombres.
    private const int MesaMaxima = 16;

    // Se mira el roster por reflexion y no pidiendo bots: PickBot filtra por
    // nombre ya usado, asi que un duplicado en la lista simplemente nunca se
    // reparte dos veces y por fuera es invisible.
    private static (string Name, BotPersonality Personality)[] Roster()
    {
        var campo = typeof(BotService).GetField("Roster", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(campo);   // si se renombra, que falle aqui y no en silencio
        return ((string Name, BotPersonality Personality)[])campo!.GetValue(null)!;
    }

    [Fact]
    public void Ningun_nombre_del_roster_esta_repetido()
    {
        var nombres = Roster().Select(b => b.Name).ToList();
        var repetidos = nombres.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(repetidos.Count == 0, $"nombres repetidos: {string.Join(", ", repetidos)}");
    }

    [Fact]
    public void El_roster_esta_repartido_entre_las_tres_personalidades()
    {
        var porPersonalidad = Roster().GroupBy(b => b.Personality).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(3, porPersonalidad.Count);
        // Ninguna personalidad puede quedarse tan corta que la mesa salga sesgada.
        Assert.All(porPersonalidad.Values, n => Assert.True(n >= MesaMaxima / 3,
            $"personalidad con solo {n} bots"));
    }

    [Fact]
    public void El_roster_llena_una_mesa_entera_sin_inventar_nombres()
    {
        var usados = new HashSet<string>();

        for (int i = 0; i < MesaMaxima; i++)
        {
            var (nombre, _) = _bots.PickBot(usados);
            Assert.False(nombre.StartsWith("Bot-"),
                $"se acabaron los nombres en el puesto {i + 1} de {MesaMaxima}");
            usados.Add(nombre);
        }
    }

    [Fact]
    public void PickBot_nunca_repite_un_nombre_ya_en_la_mesa()
    {
        var usados = new HashSet<string>();

        for (int i = 0; i < MesaMaxima; i++)
        {
            var (nombre, _) = _bots.PickBot(usados);
            Assert.DoesNotContain(nombre, usados);
            usados.Add(nombre);
        }
    }

    [Fact]
    public void Hay_bots_de_las_tres_personalidades()
    {
        var usados = new HashSet<string>();
        var personalidades = new HashSet<BotPersonality>();

        while (true)
        {
            var (nombre, personalidad) = _bots.PickBot(usados);
            if (nombre.StartsWith("Bot-")) break;
            usados.Add(nombre);
            personalidades.Add(personalidad);
            if (usados.Count > 500) break;
        }

        Assert.Equal(3, personalidades.Count);
    }

    [Theory]
    [InlineData(BotPersonality.Conservative)]
    [InlineData(BotPersonality.Balanced)]
    [InlineData(BotPersonality.Aggressive)]
    public void Un_bot_nunca_descarta_cartas_que_no_tiene(BotPersonality personalidad)
    {
        var mano = new List<Card>
        {
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Red),
            new(Guid.NewGuid(), Letter.T, Figure.Square, CardColor.Green),
            new(Guid.NewGuid(), Letter.I, Figure.Star, CardColor.Blue),
            new(Guid.NewGuid(), Letter.K, Figure.Triangle, CardColor.Cyan),
            new(Guid.NewGuid(), Letter.E, Figure.Heart, CardColor.Magenta),
        };

        var descartes = _bots.ChooseDiscards(mano, personalidad);

        Assert.All(descartes, id => Assert.Contains(mano, c => c.Id == id));
        Assert.Equal(descartes.Count, descartes.Distinct().Count());
        Assert.InRange(descartes.Count, 0, mano.Count);
    }

    // Con una jugada ya inmejorable no tiene sentido romperla.
    [Fact]
    public void Un_bot_no_deshace_un_Retriker_Fuerte()
    {
        var mano = new List<Card>
        {
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Red),
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Green),
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Blue),
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Cyan),
            new(Guid.NewGuid(), Letter.T, Figure.Circle, CardColor.Magenta),
        };

        foreach (var personalidad in Enum.GetValues<BotPersonality>())
            Assert.Empty(_bots.ChooseDiscards(mano, personalidad));
    }
}
