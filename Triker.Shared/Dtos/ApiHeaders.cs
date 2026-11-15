namespace Triker.Shared.Dtos;

// El id de un jugador o espectador es publico (sale en el estado de la partida
// que reciben todos). Lo que prueba que quien llama ocupa ese asiento es el
// token secreto que devuelven crear, unirse y ver; viaja en esta cabecera en
// cada peticion REST y como argumento de GameHub.Entrar, nunca en la URL.
public static class ApiHeaders
{
    public const string Token = "X-Triker-Token";
}
