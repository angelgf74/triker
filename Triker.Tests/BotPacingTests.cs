using Triker.Api.Services;

namespace Triker.Tests;

// Ritmo de los bots. Cada bot "pensaba" 1,2 s con 0,9 s entre uno y otro, sin
// mirar cuantos habia: tras tu jugada, 6 s con 3 bots y mas de 31 s con la mesa
// llena (15). Con pocos bots la pausa ayuda a ver que hace cada uno y se queda
// igual; a partir de 4 se reparte un tiempo total que crece despacio.
public class BotPacingTests
{
    // Lo que tarda una tanda de n bots seguidos, sin la pausa final antes de revelar.
    private static double Segundos(int n)
    {
        var (pensar, entre) = BotPacing.For(n);
        return n * pensar.TotalSeconds + (n - 1) * entre.TotalSeconds;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Con_pocos_bots_el_ritmo_no_cambia(int bots)
    {
        var (pensar, entre) = BotPacing.For(bots);

        Assert.Equal(TimeSpan.FromMilliseconds(1200), pensar);
        Assert.Equal(TimeSpan.FromMilliseconds(900), entre);
    }

    [Fact]
    public void La_mesa_llena_ya_no_tarda_medio_minuto()
    {
        Assert.InRange(Segundos(15), 8, 10.5);   // eran 30,6 s
        Assert.InRange(Segundos(7), 6, 8);       // eran 13,8 s
    }

    [Fact]
    public void Mas_bots_nunca_tardan_menos_en_total_ni_van_mas_lentos_cada_uno()
    {
        for (int n = 1; n < 15; n++)
        {
            Assert.True(Segundos(n + 1) >= Segundos(n), $"{n + 1} bots tardan menos que {n}");
            Assert.True(BotPacing.For(n + 1).Think <= BotPacing.For(n).Think, $"con {n + 1} bots cada uno piensa mas que con {n}");
        }
    }

    // Por rapido que vaya, tiene que dar tiempo a ver quien juega.
    [Fact]
    public void Ningun_bot_juega_tan_rapido_que_no_se_vea()
    {
        for (int n = 1; n <= 15; n++)
        {
            var (pensar, entre) = BotPacing.For(n);
            Assert.True(pensar >= TimeSpan.FromMilliseconds(350), $"con {n} bots piensa {pensar.TotalMilliseconds} ms");
            Assert.True(entre >= TimeSpan.FromMilliseconds(250), $"con {n} bots hay {entre.TotalMilliseconds} ms entre uno y otro");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Sin_bots_por_delante_vale_el_ritmo_normal(int bots)
        => Assert.Equal(BotPacing.For(1), BotPacing.For(bots));
}
