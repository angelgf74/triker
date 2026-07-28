using Triker.Api.Services;
using Triker.Shared.Models;

namespace Triker.Tests;

// Las manos se construyen a partir de la DEFINICION de cada jugada (la misma
// tabla que ve el jugador en el modal de ayuda), no de lo que hace el codigo:
// si se leen del codigo, la prueba solo confirma que el codigo hace lo que hace.
//
// Cada mano lleva cartas de relleno con letra, figura y color distintos entre si
// y distintos de los del grupo, para que no aparezcan grupos por accidente.
public class ScoringServiceTests
{
    private readonly ScoringService _scoring = new();

    private static Card C(Letter l, Figure f, CardColor c) => new(Guid.NewGuid(), l, f, c);

    private HandResult Evaluate(params Card[] hand)
    {
        var r = _scoring.EvaluateHand(Guid.NewGuid(), "Test", [.. hand]);
        return new HandResult(r.HandRank, r.HandName, r.TotalScore);
    }

    private record HandResult(HandRank Rank, string Name, int Score);

    // --- Cartas de relleno: no comparten nada entre si ni con los grupos ---
    private static Card Filler1 => C(Letter.A, Figure.Clover, CardColor.Black);
    private static Card Filler2 => C(Letter.G, Figure.Diamond, CardColor.Orange);
    private static Card Filler3 => C(Letter.F, Figure.Spade, CardColor.Brown);

    // ---------------------------------------------------------------- rangos

    [Fact]
    public void SinJugada_cuando_ninguna_carta_comparte_nada()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.R, Figure.Square, CardColor.Green),
            C(Letter.I, Figure.Star, CardColor.Blue),
            Filler1, Filler2);

        Assert.Equal(HandRank.SinJugada, r.Rank);
        Assert.Equal("Sin jugada", r.Name);
        Assert.Equal(0, r.Score);
    }

    [Fact]
    public void Pareja_cuando_dos_cartas_comparten_1_caracteristica()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),   // solo la letra
            C(Letter.I, Figure.Star, CardColor.Blue),
            Filler1, Filler2);

        Assert.Equal(HandRank.ParejaDebil, r.Rank);
        Assert.Equal("Pareja", r.Name);
    }

    [Fact]
    public void ParejaFuerte_cuando_dos_cartas_comparten_2_caracteristicas()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),   // letra + figura
            C(Letter.I, Figure.Star, CardColor.Blue),
            Filler1, Filler2);

        Assert.Equal(HandRank.ParejaFuerte, r.Rank);
        Assert.Equal("Pareja Fuerte", r.Name);
    }

    [Fact]
    public void DoblePareja_cuando_hay_dos_parejas_debiles()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),   // pareja por letra
            C(Letter.I, Figure.Star, CardColor.Blue),
            C(Letter.K, Figure.Star, CardColor.Cyan),      // pareja por figura
            Filler1);

        Assert.Equal(HandRank.DobleParejaDebil, r.Rank);
        Assert.Equal("Doble Pareja", r.Name);
    }

    [Fact]
    public void DobleParejaMixta_cuando_hay_una_pareja_fuerte_y_una_debil()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),   // fuerte: letra + figura
            C(Letter.I, Figure.Star, CardColor.Blue),
            C(Letter.K, Figure.Star, CardColor.Cyan),      // debil: figura
            Filler1);

        Assert.Equal(HandRank.DobleParejaMedia, r.Rank);
        Assert.Equal("Doble Pareja Mixta", r.Name);
    }

    [Fact]
    public void DobleParejaFuerte_cuando_hay_dos_parejas_fuertes()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.I, Figure.Star, CardColor.Blue),
            C(Letter.I, Figure.Star, CardColor.Cyan),
            Filler1);

        Assert.Equal(HandRank.DobleParejaFuerte, r.Rank);
        Assert.Equal("Doble Pareja Fuerte", r.Name);
    }

    [Fact]
    public void Trio_cuando_tres_cartas_comparten_1_caracteristica()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),      // solo la letra
            Filler1, Filler2);

        Assert.Equal(HandRank.TrioDebil, r.Rank);
        Assert.Equal("Trío", r.Name);
    }

    [Fact]
    public void TrioFuerte_cuando_tres_cartas_comparten_2_caracteristicas()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),    // letra + figura
            Filler1, Filler2);

        Assert.Equal(HandRank.TrioFuerte, r.Rank);
        Assert.Equal("Trío Fuerte", r.Name);
    }

    [Fact]
    public void Full_cuando_hay_trio_debil_y_pareja_debil()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),      // trio por letra
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.E, Figure.Triangle, CardColor.Magenta)); // pareja por figura

        Assert.Equal(HandRank.FullDebil, r.Rank);
        Assert.Equal("Full", r.Name);
    }

    [Fact]
    public void FullMixto2_cuando_hay_trio_debil_y_pareja_fuerte()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),      // trio por letra
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.K, Figure.Triangle, CardColor.Magenta)); // pareja letra + figura

        Assert.Equal(HandRank.FullMixto2, r.Rank);
        Assert.Equal("Full Mixto 2", r.Name);
    }

    [Fact]
    public void FullMixto1_cuando_hay_trio_fuerte_y_pareja_debil()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),    // trio letra + figura
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.E, Figure.Triangle, CardColor.Magenta)); // pareja por figura

        Assert.Equal(HandRank.FullMixto1, r.Rank);
        Assert.Equal("Full Mixto 1", r.Name);
    }

    [Fact]
    public void FullFuerte_cuando_hay_trio_fuerte_y_pareja_fuerte()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.K, Figure.Triangle, CardColor.Magenta));

        Assert.Equal(HandRank.FullFuerte, r.Rank);
        Assert.Equal("Full Fuerte", r.Name);
    }

    [Fact]
    public void Triker_cuando_cuatro_cartas_comparten_1_caracteristica()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),
            C(Letter.T, Figure.Triangle, CardColor.Cyan),  // solo la letra
            Filler1);

        Assert.Equal(HandRank.TrikerDebil, r.Rank);
        Assert.Equal("Triker", r.Name);
    }

    [Fact]
    public void TrikerFuerte_cuando_cuatro_cartas_comparten_2_caracteristicas()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),
            C(Letter.T, Figure.Circle, CardColor.Cyan),    // letra + figura
            Filler1);

        Assert.Equal(HandRank.TrikerFuerte, r.Rank);
        Assert.Equal("Triker Fuerte", r.Name);
    }

    [Fact]
    public void Retriker_cuando_las_cinco_comparten_1_caracteristica()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),
            C(Letter.T, Figure.Triangle, CardColor.Cyan),
            C(Letter.T, Figure.Heart, CardColor.Magenta));

        Assert.Equal(HandRank.RetrikerDebil, r.Rank);
        Assert.Equal("Retriker", r.Name);
    }

    [Fact]
    public void RetrikerFuerte_cuando_las_cinco_comparten_2_caracteristicas()
    {
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),
            C(Letter.T, Figure.Circle, CardColor.Cyan),
            C(Letter.T, Figure.Circle, CardColor.Magenta));

        Assert.Equal(HandRank.RetrikerFuerte, r.Rank);
        Assert.Equal("Retriker Fuerte", r.Name);
    }

    // -------------------------------------------------------------- puntuacion

    // CLAUDE.md: un grupo puntua (N-1)^2 * 2^A, N = tamaño y A = atributos que comparte.
    [Theory]
    [InlineData(2, 1, 2)]    // Pareja:          1 * 2
    [InlineData(2, 2, 4)]    // Pareja Fuerte:   1 * 4
    [InlineData(3, 1, 8)]    // Trio:            4 * 2
    [InlineData(3, 2, 16)]   // Trio Fuerte:     4 * 4
    [InlineData(4, 1, 18)]   // Triker:          9 * 2
    [InlineData(4, 2, 36)]   // Triker Fuerte:   9 * 4
    [InlineData(5, 1, 32)]   // Retriker:       16 * 2
    [InlineData(5, 2, 64)]   // Retriker Fuerte:16 * 4
    public void Un_grupo_puntua_segun_la_formula(int tam, int atributos, int esperado)
    {
        var colores = new[] { CardColor.Red, CardColor.Green, CardColor.Blue, CardColor.Cyan, CardColor.Magenta };
        var figuras = new[] { Figure.Circle, Figure.Square, Figure.Star, Figure.Triangle, Figure.Heart };

        // atributos=2 => comparten letra y figura; atributos=1 => solo la letra.
        var grupo = Enumerable.Range(0, tam)
            .Select(i => C(Letter.T, atributos == 2 ? Figure.Circle : figuras[i], colores[i]))
            .ToList();

        var relleno = new[] { Filler1, Filler2, Filler3 }.Take(5 - tam);
        var r = Evaluate([.. grupo, .. relleno]);

        Assert.Equal(esperado, r.Score);
    }

    // CLAUDE.md: el ganador se decide SOLO por HandRank, TotalScore no ordena.
    // Trio y Doble Pareja Fuerte puntuan igual (8) pero el Trio es mejor jugada.
    [Fact]
    public void Los_puntos_no_deciden_el_ganador_el_rango_si()
    {
        var trio = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Square, CardColor.Green),
            C(Letter.T, Figure.Star, CardColor.Blue),
            Filler1, Filler2);

        var doblePareja = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.I, Figure.Star, CardColor.Blue),
            C(Letter.I, Figure.Star, CardColor.Cyan),
            Filler1);

        Assert.Equal(trio.Score, doblePareja.Score);          // mismos puntos...
        Assert.True(trio.Rank > doblePareja.Rank);            // ...distinta jugada
    }

    // El buscador tiene que considerar el trio de 3 (a=1) MAS una pareja suelta,
    // en vez de quedarse con la pareja de 2 atributos que vive dentro del trio.
    [Fact]
    public void Prefiere_el_trio_mas_pareja_a_la_pareja_fuerte_contenida_en_el_trio()
    {
        // c1+c2 comparten figura y color (pareja fuerte), y con c3 forman trio por figura.
        var r = Evaluate(
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.R, Figure.Circle, CardColor.Red),
            C(Letter.I, Figure.Circle, CardColor.Blue),
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.K, Figure.Triangle, CardColor.Magenta));

        // Quedarse con las dos parejas fuertes daria Doble Pareja Fuerte (peor).
        Assert.Equal(HandRank.FullMixto2, r.Rank);
        Assert.True(r.Rank > HandRank.DobleParejaFuerte);
    }

    [Fact]
    public void Los_grupos_devueltos_no_comparten_cartas()
    {
        var res = _scoring.EvaluateHand(Guid.NewGuid(), "Test", [
            C(Letter.T, Figure.Circle, CardColor.Red),
            C(Letter.T, Figure.Circle, CardColor.Green),
            C(Letter.T, Figure.Circle, CardColor.Blue),
            C(Letter.K, Figure.Triangle, CardColor.Cyan),
            C(Letter.K, Figure.Triangle, CardColor.Magenta)]);

        var ids = res.Groups.SelectMany(g => g.Cards.Select(c => c.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
