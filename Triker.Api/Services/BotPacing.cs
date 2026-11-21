namespace Triker.Api.Services;

// Cuanto "piensa" cada bot y cuanto se espera entre uno y el siguiente.
//
// Antes era fijo (1,2 s y 0,9 s) sin mirar cuantos bots habia: tras la jugada
// del humano, 6 s con 3 bots pero mas de 31 s con la mesa llena (15), con la
// peticion del descarte esperando todo ese rato.
//
// Con hasta 3 bots seguidos no cambia: esa pausa es la que deja ver que hace
// cada uno, y es el caso de «Jugar contra bots». A partir de 4 la tanda entera
// tiene un tiempo total que crece despacio (0,3 s por bot de mas), repartido
// entre todos con la misma proporcion entre pensar y esperar.
public static class BotPacing
{
    private const int SinPrisa = 3;                 // hasta aqui, el ritmo de siempre
    private const double Pensar = 1.2;              // segundos, ritmo de siempre
    private const double Entre = 0.9;
    private const double PorBotDeMas = 0.3;         // lo que alarga la tanda cada bot a partir del cuarto

    // Por rapido que vaya, tiene que verse quien juega.
    private static readonly TimeSpan PensarMinimo = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan EntreMinimo = TimeSpan.FromMilliseconds(250);

    // botsInARow: los bots que van a jugar seguidos desde el turno actual
    // (GameService.TurnOf).
    public static (TimeSpan Think, TimeSpan Gap) For(int botsInARow)
    {
        if (botsInARow <= SinPrisa)
            return (TimeSpan.FromSeconds(Pensar), TimeSpan.FromSeconds(Entre));

        // Tiempo de la tanda si cada bot tuviera su pausa de despues: con 3
        // bots son 3 x 2,1 s, y desde ahi sube 0,3 s por bot.
        var tanda = SinPrisa * (Pensar + Entre) + (botsInARow - SinPrisa) * PorBotDeMas;
        var porBot = tanda / botsInARow;
        var pensar = TimeSpan.FromSeconds(porBot * Pensar / (Pensar + Entre));
        var entre = TimeSpan.FromSeconds(porBot * Entre / (Pensar + Entre));
        return (pensar < PensarMinimo ? PensarMinimo : pensar, entre < EntreMinimo ? EntreMinimo : entre);
    }
}
