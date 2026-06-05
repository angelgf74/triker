namespace Triker.Shared.Models;

public enum Letter { T, R, I, K, E }
public enum Figure { Circle, Square, Star, Triangle, Heart, Clover }
public enum CardColor { Red, Green, Blue, Cyan, Magenta, Black }
public enum GameStatus { Waiting, Playing, Revealing, Finished }
public enum PlayerTurnStatus { Thinking, Ready }

public enum HandRank
{
    SinJugada = 0,
    ParejaSimple = 1,
    EscaleraSimple = 2,
    DobleParejaSimple = 3,
    ParejaDoble = 4,
    ParejaDobleConParejaSimple = 5,
    TrioSimple = 6,
    DobleParejaDoble = 7,
    TrioSimpleConParejaSimple = 8,
    TrioSimpleConParejaDoble = 9,
    EscaleraDoble = 10,
    TrioDoble = 11,
    TrioDobleConParejaSimple = 12,
    TrikerSimple = 13,
    TrioDobleConParejaDoble = 14,
    RetrikerSimple = 15,
    TrikerDoble = 16,
    EscaleraReal = 17,
    RetrikerDoble = 18
}
