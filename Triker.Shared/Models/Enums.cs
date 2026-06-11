namespace Triker.Shared.Models;

public enum Letter { T, R, I, K, E }
public enum Figure { Circle, Square, Star, Triangle, Heart, Clover }
public enum CardColor { Red, Green, Blue, Cyan, Magenta, Black }
public enum GameStatus { Waiting, Playing, Revealing, Finished }
public enum PlayerTurnStatus { Thinking, Ready }

public enum HandRank
{
    SinJugada = 0,
    ParejaDebil = 1,
    TrioDebil = 2,
    DobleParejaDebil = 3,
    TrikerDebil = 4,
    FullDebil = 5,
    RetrikerDebil = 6,
    ParejaFuerte = 7,
    DobleParejaMedia = 8,
    FullMixto = 9,
    TrioFuerte = 10,
    DobleParejaFuerte = 11,
    Full = 12,
    FullFuerte = 13,
    TrikerFuerte = 14,
    RetrikerFuerte = 15,
    Escalera = 16,
}
