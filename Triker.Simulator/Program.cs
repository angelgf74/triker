using System.Globalization;
using ClosedXML.Excel;
using Triker.Shared.Models;

// Programa auxiliar de Triker.
// Simula muchas manos para estimar la frecuencia de cada jugada al variar
// el número de valores por parámetro (letras/figuras/colores). Sirve para
// decidir si conviene subir ese número en el juego.

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Simulador de jugadas de Triker ===");
Console.WriteLine();

int iteraciones = PedirEntero("Número de iteraciones (manos a simular): ", min: 1, max: 1_000_000_000);

const int tamanoMano = 5;
const int nMin = 5;
const int nMax = 8;

var rng = Random.Shared;
var mano = new Card[tamanoMano];

// Conteo por valor de n -> (jugada -> apariciones).
var conteoPorN = new Dictionary<int, Dictionary<HandRank, long>>();

for (int n = nMin; n <= nMax; n++)
{
    int tamanoMazo = n * n * n;
    Console.WriteLine();
    Console.WriteLine($"n={n}: mazo de {tamanoMazo} cartas ({n}×{n}×{n}). Simulando {iteraciones:N0} manos...");

    var conteo = Enum.GetValues<HandRank>().ToDictionary(r => r, _ => 0L);
    var mazo = ConstruirMazo(n);

    long siguienteAviso = Math.Max(1, iteraciones / 20);
    for (long it = 0; it < iteraciones; it++)
    {
        RepartirMano(mazo, mano, rng);
        conteo[EvaluarMano(mano)]++;

        if (it + 1 >= siguienteAviso)
        {
            Console.Write($"\r  {(it + 1):N0} / {iteraciones:N0} ({(it + 1) * 100.0 / iteraciones:F0}%)   ");
            siguienteAviso += Math.Max(1, iteraciones / 20);
        }
    }
    Console.WriteLine($"\r  {iteraciones:N0} / {iteraciones:N0} (100%)        ");

    conteoPorN[n] = conteo;
}

string ruta = ExportarExcel(nMin, nMax, iteraciones, conteoPorN);
Console.WriteLine();
Console.WriteLine($"Fichero generado: {ruta}");


// ---------------------------------------------------------------------------
// Construcción del mazo y reparto
// ---------------------------------------------------------------------------

static Card[] ConstruirMazo(int n)
{
    var mazo = new Card[n * n * n];
    int i = 0;
    for (int l = 0; l < n; l++)
        for (int f = 0; f < n; f++)
            for (int c = 0; c < n; c++)
                mazo[i++] = new Card(l, f, c);
    return mazo;
}

// Selecciona 'mano.Length' cartas distintas del mazo mediante un Fisher-Yates
// parcial sobre el propio array (solo se barajan las primeras k posiciones).
static void RepartirMano(Card[] mazo, Card[] mano, Random rng)
{
    int k = mano.Length;
    for (int i = 0; i < k; i++)
    {
        int j = rng.Next(i, mazo.Length);
        (mazo[i], mazo[j]) = (mazo[j], mazo[i]);
        mano[i] = mazo[i];
    }
}


// ---------------------------------------------------------------------------
// Evaluación de la mano (réplica de Triker.Api/Services/ScoringService.cs)
// ---------------------------------------------------------------------------

static HandRank EvaluarMano(Card[] mano)
{
    var grupos = GruposCandidatos(mano);
    var mejores = MejorAgrupacion(mano, grupos);
    return CalcularRank(mejores);
}

static List<Grupo> GruposCandidatos(Card[] mano)
{
    var grupos = new List<Grupo>();

    // Todos los subconjuntos de tamaño >= 2 (igual que el ScoringService original),
    // representados como máscaras de bits sobre las 5 posiciones de la mano.
    int total = 1 << mano.Length;
    for (int mask = 1; mask < total; mask++)
    {
        int count = System.Numerics.BitOperations.PopCount((uint)mask);
        if (count < 2) continue;

        int a = ParametrosCoincidentes(mano, mask);
        if (a == 0) continue;

        grupos.Add(new Grupo(mask, count, a, (count - 1) * (count - 1) * (1 << a)));
    }

    return grupos;
}

// Con 5 cartas hay como mucho 2 grupos no solapados (2+3 o 2+2); una búsqueda
// O(k²) es completa. Se elige por rango, igual que el original.
static List<Grupo> MejorAgrupacion(Card[] mano, List<Grupo> candidatos)
{
    if (candidatos.Count == 0) return [];

    var mejor = new List<Grupo>();
    HandRank mejorRank = HandRank.SinJugada;

    foreach (var g in candidatos)
    {
        var rank = CalcularRank([g]);
        if (rank > mejorRank) { mejorRank = rank; mejor = [g]; }
    }

    for (int i = 0; i < candidatos.Count; i++)
        for (int j = i + 1; j < candidatos.Count; j++)
        {
            if ((candidatos[i].Mask & candidatos[j].Mask) != 0) continue;
            var actual = new List<Grupo> { candidatos[i], candidatos[j] };
            var rank = CalcularRank(actual);
            if (rank > mejorRank) { mejorRank = rank; mejor = actual; }
        }

    return mejor;
}

static HandRank CalcularRank(List<Grupo> grupos)
{
    if (grupos.Count == 0) return HandRank.SinJugada;

    var key = grupos
        .Select(g => (n: g.Count, a: g.MatchingParams))
        .OrderByDescending(x => x.n).ThenByDescending(x => x.a)
        .ToList();

    return key switch
    {
        [(5, 2)] => HandRank.RetrikerFuerte,
        [(5, 1)] => HandRank.RetrikerDebil,
        [(4, 2)] => HandRank.TrikerFuerte,
        [(4, 1)] => HandRank.TrikerDebil,
        [(3, 2), (2, 2)] => HandRank.FullFuerte,
        [(3, 2), (2, 1)] => HandRank.FullMixto1,
        [(3, 1), (2, 2)] => HandRank.FullMixto2,
        [(3, 1), (2, 1)] => HandRank.FullDebil,
        [(3, 2)] => HandRank.TrioFuerte,
        [(3, 1)] => HandRank.TrioDebil,
        [(2, 2), (2, 2)] => HandRank.DobleParejaFuerte,
        [(2, 2), (2, 1)] => HandRank.DobleParejaMedia,
        [(2, 2)] => HandRank.ParejaFuerte,
        [(2, 1), (2, 1)] => HandRank.DobleParejaDebil,
        [(2, 1)] => HandRank.ParejaDebil,
        _ => HandRank.SinJugada
    };
}

static int ParametrosCoincidentes(Card[] mano, int mask)
{
    int first = -1;
    bool sameL = true, sameF = true, sameC = true;
    Card baseCard = default;

    for (int i = 0; i < mano.Length; i++)
    {
        if ((mask & (1 << i)) == 0) continue;
        if (first == -1) { first = i; baseCard = mano[i]; continue; }
        if (mano[i].L != baseCard.L) sameL = false;
        if (mano[i].F != baseCard.F) sameF = false;
        if (mano[i].C != baseCard.C) sameC = false;
    }

    return (sameL ? 1 : 0) + (sameF ? 1 : 0) + (sameC ? 1 : 0);
}


// ---------------------------------------------------------------------------
// Exportación a Excel
// ---------------------------------------------------------------------------

static string ExportarExcel(int nMin, int nMax, long iteraciones, Dictionary<int, Dictionary<HandRank, long>> conteoPorN)
{
    string fecha = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
    string nombre = $"Triker_Simulacion_n{nMin}-{nMax}_iter{iteraciones}_{fecha}.xlsx";
    string ruta = Path.Combine(Environment.CurrentDirectory, nombre);

    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Jugadas");

    // Cabecera con los parámetros de la simulación.
    ws.Cell(1, 1).Value = "Iteraciones por valor de n";
    ws.Cell(1, 2).Value = iteraciones;
    ws.Cell(2, 1).Value = "Fecha";
    ws.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // Cabecera de la tabla en dos niveles:
    //   fila 4:  [vacío] | "n (mazo)" combinado sobre 2 columnas | ...
    //   fila 5:  "Jugada" | "Apariciones" | "Porcentaje" | ...
    int filaGrupo = 4;
    int filaSub = 5;
    ws.Cell(filaSub, 1).Value = "Jugada";
    int col = 2;
    for (int n = nMin; n <= nMax; n++)
    {
        int tamanoMazo = n * n * n;
        var celdaGrupo = ws.Cell(filaGrupo, col);
        celdaGrupo.Value = $"{n} ({tamanoMazo})";
        ws.Range(filaGrupo, col, filaGrupo, col + 1).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(filaSub, col).Value = "Apariciones";
        ws.Cell(filaSub, col + 1).Value = "Porcentaje";
        col += 2;
    }
    int ultimaCol = col - 1;
    ws.Range(filaGrupo, 1, filaSub, ultimaCol).Style.Font.Bold = true;
    int fila = filaSub + 1;

    // Una fila por jugada, ordenadas de mayor a menor rango (mejor jugada primero).
    foreach (var rank in Enum.GetValues<HandRank>().OrderByDescending(r => (int)r))
    {
        ws.Cell(fila, 1).Value = NombreJugada(rank);
        col = 2;
        for (int n = nMin; n <= nMax; n++)
        {
            long apariciones = conteoPorN[n][rank];
            ws.Cell(fila, col).Value = apariciones;
            ws.Cell(fila, col + 1).Value = (double)apariciones / iteraciones;
            ws.Cell(fila, col + 1).Style.NumberFormat.Format = "0.0000%";
            col += 2;
        }
        fila++;
    }

    // Fila de totales.
    ws.Cell(fila, 1).Value = "TOTAL";
    col = 2;
    for (int n = nMin; n <= nMax; n++)
    {
        ws.Cell(fila, col).Value = iteraciones;
        ws.Cell(fila, col + 1).Value = 1.0;
        ws.Cell(fila, col + 1).Style.NumberFormat.Format = "0.0000%";
        col += 2;
    }
    ws.Range(fila, 1, fila, ultimaCol).Style.Font.Bold = true;

    ws.Columns().AdjustToContents();
    wb.SaveAs(ruta);

    return ruta;
}

static string NombreJugada(HandRank rank) => rank switch
{
    HandRank.ParejaDebil       => "Pareja",
    HandRank.TrioDebil         => "Trío",
    HandRank.DobleParejaDebil  => "Doble Pareja",
    HandRank.TrikerDebil       => "Triker",
    HandRank.FullDebil         => "Full",
    HandRank.RetrikerDebil     => "Retriker",
    HandRank.ParejaFuerte      => "Pareja Fuerte",
    HandRank.DobleParejaMedia  => "Doble Pareja Mixta",
    HandRank.FullMixto2        => "Full Mixto 2",
    HandRank.TrioFuerte        => "Trío Fuerte",
    HandRank.DobleParejaFuerte => "Doble Pareja Fuerte",
    HandRank.FullMixto1        => "Full Mixto 1",
    HandRank.FullFuerte        => "Full Fuerte",
    HandRank.TrikerFuerte      => "Triker Fuerte",
    HandRank.RetrikerFuerte    => "Retriker Fuerte",
    _                          => "Sin jugada"
};


// ---------------------------------------------------------------------------
// Utilidades de entrada
// ---------------------------------------------------------------------------

static int PedirEntero(string mensaje, int min, int max)
{
    while (true)
    {
        Console.Write(mensaje);
        string? linea = Console.ReadLine();
        if (linea is null) // EOF / sin entrada disponible
        {
            Console.WriteLine();
            Console.WriteLine("No hay entrada disponible. Saliendo.");
            Environment.Exit(1);
        }
        if (int.TryParse(linea, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor)
            && valor >= min && valor <= max)
        {
            return valor;
        }
        Console.WriteLine($"  Introduce un número entero entre {min} y {max}.");
    }
}


// Carta del simulador: índices enteros para permitir un número arbitrario de
// valores por parámetro (sin el límite de los enums de Triker.Shared).
readonly record struct Card(int L, int F, int C);

// Grupo candidato representado por su máscara de bits sobre la mano.
readonly record struct Grupo(int Mask, int Count, int MatchingParams, int Score);
