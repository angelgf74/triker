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
