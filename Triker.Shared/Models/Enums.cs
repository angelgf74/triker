namespace Triker.Shared.Models;

public enum Letter { T, R, I, K, E, A, G, F }
public enum Figure { Circle, Square, Star, Triangle, Heart, Clover, Diamond, Spade }
public enum CardColor { Red, Green, Blue, Cyan, Magenta, Black, Orange, Brown }
public enum GameStatus { Waiting, Playing, Revealing, Finished }
public enum PlayerTurnStatus { Thinking, Ready }

public enum HandRank
{
    SinJugada = 0,
    ParejaDebil = 1,
    DobleParejaDebil = 2,
    ParejaFuerte = 3,
    DobleParejaMedia = 4,
    DobleParejaFuerte = 5,
    TrioDebil = 6,
    FullDebil = 7,
    FullMixto2 = 8,
    TrioFuerte = 9,
    FullMixto1 = 10,
    TrikerDebil = 11,
    FullFuerte = 12,
    RetrikerDebil = 13,
    TrikerFuerte = 14,
    RetrikerFuerte = 15,
}
