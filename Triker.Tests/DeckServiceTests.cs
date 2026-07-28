using Triker.Api.Services;
using Triker.Shared.Models;

namespace Triker.Tests;

public class DeckServiceTests
{
    private readonly DeckService _deck = new();

    // CLAUDE.md: la baraja son N^3 cartas, tomando los N primeros valores de
    // cada atributo. Con N=8 (el valor por defecto) salen 512.
    [Theory]
    [InlineData(5, 125)]
    [InlineData(6, 216)]
    [InlineData(7, 343)]
    [InlineData(8, 512)]
    public void La_baraja_tiene_n_al_cubo_cartas(int n, int esperado)
        => Assert.Equal(esperado, _deck.GenerateDeck(n).Count);

    [Fact]
    public void No_hay_dos_cartas_con_la_misma_combinacion()
    {
        var cartas = _deck.GenerateDeck(8);
        var combinaciones = cartas.Select(c => (c.Letter, c.Figure, c.Color)).ToHashSet();

        Assert.Equal(cartas.Count, combinaciones.Count);
    }

    [Fact]
    public void Cada_carta_tiene_un_id_unico()
    {
        var cartas = _deck.GenerateDeck(8);
        Assert.Equal(cartas.Count, cartas.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void La_baraja_recortada_solo_usa_los_primeros_valores()
    {
        var cartas = _deck.GenerateDeck(5);
        var permitidas = Enum.GetValues<Letter>().Take(5).ToHashSet();

        Assert.All(cartas, c => Assert.Contains(c.Letter, permitidas));
    }

    [Fact]
    public void Barajar_conserva_todas_las_cartas()
    {
        var original = _deck.GenerateDeck(6);
        var barajada = _deck.Shuffle(original);

        Assert.Equal(original.Count, barajada.Count);
        Assert.Equal(original.Select(c => c.Id).ToHashSet(), barajada.Select(c => c.Id).ToHashSet());
    }

    [Fact]
    public void Robar_saca_del_mazo_y_deja_el_resto()
    {
        var mazo = _deck.GenerateDeck(5);
        var (robadas, resto) = _deck.Draw(mazo, [], 5);

        Assert.Equal(5, robadas.Count);
        Assert.Equal(mazo.Count - 5, resto.Count);
        Assert.Empty(robadas.Select(c => c.Id).Intersect(resto.Select(c => c.Id)));
    }

    // Cuando el mazo se queda corto se rebaraja el descarte: si no, la partida
    // se quedaria sin cartas que repartir.
    [Fact]
    public void Robar_con_el_mazo_corto_tira_del_descarte()
    {
        var todas = _deck.GenerateDeck(5);
        var mazo = todas.Take(2).ToList();
        var descarte = todas.Skip(2).Take(10).ToList();

        var (robadas, _) = _deck.Draw(mazo, descarte, 5);

        Assert.Equal(5, robadas.Count);
    }
}
