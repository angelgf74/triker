using Triker.Api.Services;

namespace Triker.Tests;

// Lo que la API acepta como nombre: lo mismo que deja escribir la web (20 y 30
// caracteres), limpio de caracteres de control y espacios sobrantes.
public class LimitesTests
{
    [Theory]
    [InlineData("Ana", "Ana")]
    [InlineData("  Ana  ", "Ana")]
    [InlineData("Ana   María", "Ana María")]
    [InlineData("Ana\tMaría", "Ana María")]
    [InlineData("A\u0000n\u0007a", "Ana")]
    [InlineData("<script>", "<script>")] // se acepta tal cual: Blazor lo pinta como texto
    public void Un_nombre_valido_se_normaliza(string entrada, string esperado)
        => Assert.Equal(esperado, Limites.Nombre(entrada, Limites.NombreJugador));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000\u0001")]
    public void Un_nombre_vacio_no_vale(string? entrada)
        => Assert.Null(Limites.Nombre(entrada, Limites.NombreJugador));

    // Caracteres que no se ven pero cuentan como texto. Se aceptaba un nombre
    // hecho solo de ellos (un jugador sin nombre en la mesa) y el que invierte
    // el sentido de la escritura, que descoloca lo que venga detras en la lista.
    [Theory]
    [InlineData("\u200B")]              // espacio de ancho cero
    [InlineData("\u200B\u200B\u200B")]
    [InlineData("\u202E")]              // invierte el texto (derecha a izquierda)
    [InlineData("\uFEFF")]              // marca de orden de bytes
    [InlineData("\u00AD")]              // guion opcional
    [InlineData("\u3164")]              // relleno hangul: cuenta como letra y no pinta nada
    [InlineData("\u2800")]              // braille en blanco
    [InlineData(" \u200B \u3164 ")]
    public void Un_nombre_hecho_solo_de_caracteres_invisibles_no_vale(string entrada)
        => Assert.Null(Limites.Nombre(entrada, Limites.NombreJugador));

    [Theory]
    [InlineData("A\u200Bna", "Ana")]                 // escondido en medio
    [InlineData("\u202EAna", "Ana")]                 // ya no invierte nada
    [InlineData("Ana\u2066\u2069", "Ana")]           // aislantes de direccion
    [InlineData("An\u3164a", "Ana")]
    public void Los_caracteres_invisibles_se_quitan_del_nombre(string entrada, string esperado)
        => Assert.Equal(esperado, Limites.Nombre(entrada, Limites.NombreJugador));

    // Lo que si se ve se queda como esta: tildes, otros alfabetos y emojis.
    [Theory]
    [InlineData("Ángel")]
    [InlineData("José María")]
    [InlineData("Ñandú")]
    [InlineData("Zoë")]
    [InlineData("علي")]
    [InlineData("山田")]
    [InlineData("Ana 🎴")]
    [InlineData("❤️")]                  // lleva un selector de variacion, que no es invisible por si solo
    public void Un_nombre_visible_no_cambia(string nombre)
        => Assert.Equal(nombre, Limites.Nombre(nombre, Limites.NombreJugador));

    // Sin los invisibles, el maximo cuenta lo que se ve.
    [Fact]
    public void Los_invisibles_no_cuentan_para_el_maximo()
    {
        var justo = new string('a', Limites.NombreJugador);

        Assert.Equal(justo, Limites.Nombre("\u200B" + justo + "\u200B", Limites.NombreJugador));
    }

    [Fact]
    public void El_maximo_cuenta_tras_limpiar_y_no_admite_uno_mas()
    {
        var justo = new string('a', Limites.NombreJugador);

        Assert.Equal(justo, Limites.Nombre($"  {justo}  ", Limites.NombreJugador));
        Assert.Null(Limites.Nombre(justo + "a", Limites.NombreJugador));
        Assert.Null(Limites.Nombre(new string('x', 200_000), Limites.NombrePartida));
    }

    [Fact]
    public void Un_token_vacio_nunca_coincide()
    {
        Assert.False(Limites.MismoToken(null, null));
        Assert.False(Limites.MismoToken("", ""));
        Assert.False(Limites.MismoToken(null, "algo"));
        Assert.True(Limites.MismoToken("abc", "abc"));
        Assert.False(Limites.MismoToken("abc", "abd"));
    }
}
