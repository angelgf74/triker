using Triker.Shared.Models;

namespace Triker.Web.Services;

// Puesto de una jugada en la escala de jugadas.
//
// El enum HandRank va de peor (SinJugada = 0) a mejor (RetrikerFuerte = 15),
// pero la tabla del modal de ayuda numera al reves: 1 es la mejor. Se respeta
// esa numeracion para que el numero que sale en los resultados sea el mismo que
// el jugador ve en la tabla de la ayuda (ver HelpModal.razor).
public static class HandLadder
{
    public static readonly int Total = Enum.GetValues<HandRank>().Length;

    public static int Position(HandRank rank) => Total - (int)rank;

    public static string Label(HandRank rank) => $"Jugada nº {Position(rank)} de {Total} — 1 es la mejor";
}
