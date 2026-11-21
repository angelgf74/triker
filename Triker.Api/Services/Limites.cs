using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Triker.Api.Services;

// Lo que la API acepta de fuera. Los maximos de nombre coinciden con el
// maxlength de los campos de la web, asi que un cliente normal nunca los toca:
// solo frenan a quien llama a la API a mano (se aceptaban nombres de 200.000
// caracteres, que iban a memoria, al snapshot y a cada difusion del lobby).
public static class Limites
{
    public const int NombreJugador = 20;
    public const int NombrePartida = 30;
    public const int MetaMaxima = 50;
    public const int PartidasMaximas = 500;
    // Partidas abiertas a la vez creadas desde una misma IP. Holgado para una
    // clase o un bar detras de una sola conexion; con el, llenar el tope global
    // pide una docena larga de IP en vez de una o dos.
    public const int PartidasPorIp = 40;
    public const int EspectadoresMaximos = 30;
    public const int CuerpoMaximoBytes = 16 * 1024;

    // Quita caracteres de control, junta espacios y recorta. Null si queda
    // vacio o pasa del maximo: el controlador responde 400.
    public static string? Nombre(string? valor, int maximo)
    {
        if (valor is null) return null;
        var sb = new StringBuilder(valor.Length);
        var espacio = false;
        foreach (var c in valor)
        {
            // Antes que IsControl: el tabulador y el salto de linea son las dos
            // cosas, y deben contar como espacio, no desaparecer.
            if (char.IsWhiteSpace(c))
            {
                espacio = sb.Length > 0;
                continue;
            }
            if (char.IsControl(c) || EsInvisible(c)) continue;
            if (espacio) sb.Append(' ');
            espacio = false;
            sb.Append(c);
        }
        return sb.Length == 0 || sb.Length > maximo ? null : sb.ToString();
    }

    // Lo que cuenta como texto y no pinta nada. Con ellos se podia entrar en
    // una mesa sin nombre visible, o con el caracter que invierte el sentido
    // de la escritura (U+202E), que descoloca lo que venga detras en la lista.
    //   - Categoria Format: espacio de ancho cero, marcas e inversores de
    //     direccion, guion opcional, marca de orden de bytes... Tambien cae el
    //     unidor de ancho cero, asi que un emoji compuesto queda en sus piezas.
    //   - Unos pocos que Unicode clasifica como letra o simbolo y salen en
    //     blanco: rellenos hangul y el braille vacio.
    private static bool EsInvisible(char c)
        => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format
           || c is '\u115F' or '\u1160' or '\u3164' or '\uFFA0' or '\u2800';

    // Seis cifras: con el limite de peticiones por IP de Program.cs, probar el
    // millon de codigos cuesta semanas. Con cuatro eran 9.000 y salian en segundos.
    public static string NuevoCodigo() => RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

    public static string NuevoToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    // Comparacion en tiempo constante: el tiempo de respuesta no dice cuantos
    // caracteres del token acerto quien lo prueba.
    public static bool MismoToken(string? esperado, string? recibido)
        => !string.IsNullOrEmpty(esperado) && recibido is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(recibido));
}
