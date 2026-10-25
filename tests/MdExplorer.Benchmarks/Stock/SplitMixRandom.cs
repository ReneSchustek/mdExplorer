namespace MdExplorer.Benchmarks.Stock;

/// <summary>
/// Zahlenfolge aus einem festen Startwert (SplitMix64), auf jedem Rechner und in jeder .NET-Fassung
/// dieselbe.
/// </summary>
/// <remarks>
/// <see cref="Random"/> sagt für einen festen Startwert keine Folge über Laufzeitfassungen hinweg zu.
/// Ein Messbestand, der sich mit dem nächsten SDK still ändert, machte die Messreihe wertlos: Ein
/// Sprung in den Zahlen sähe aus wie eine Verlangsamung. Für Sicherheit taugt diese Folge nicht und
/// soll es nicht.
/// </remarks>
internal sealed class SplitMixRandom(ulong seed)
{
    // Die oberen 53 Bit füllen die Mantisse eines double vollständig; mehr hat er nicht.
    private const int MantissaBits = 53;
    private const int DiscardedBits = 64 - MantissaBits;
    private const double UnitScale = 1.0 / (1UL << MantissaBits);

    // Konstanten aus der Veröffentlichung von SplitMix64 (Steele, Lea, Flood 2014).
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
    private const ulong FirstMultiplier = 0xBF58476D1CE4E5B9UL;
    private const ulong SecondMultiplier = 0x94D049BB133111EBUL;
    private const int FirstShift = 30;
    private const int SecondShift = 27;
    private const int FinalShift = 31;

    private ulong _state = seed;

    /// <summary>Gleichverteilte Zahl in [0, 1).</summary>
    /// <returns>Die nächste Zahl der Folge.</returns>
    public double NextDouble() => (NextUInt64() >> DiscardedBits) * UnitScale;

    /// <summary>Gleichverteilte ganze Zahl in [<paramref name="minValue"/>, <paramref name="maxValue"/>).</summary>
    /// <param name="minValue">Untere Grenze, eingeschlossen.</param>
    /// <param name="maxValue">Obere Grenze, ausgeschlossen.</param>
    /// <returns>Die nächste Zahl der Folge.</returns>
    public int Next(int minValue, int maxValue) => minValue + (int)(NextDouble() * (maxValue - minValue));

    /// <summary>Gleichverteilte ganze Zahl in [0, <paramref name="maxValue"/>).</summary>
    /// <param name="maxValue">Obere Grenze, ausgeschlossen.</param>
    /// <returns>Die nächste Zahl der Folge.</returns>
    public int Next(int maxValue) => Next(0, maxValue);

    private ulong NextUInt64()
    {
        _state += GoldenGamma;
        ulong mixed = _state;
        mixed = (mixed ^ (mixed >> FirstShift)) * FirstMultiplier;
        mixed = (mixed ^ (mixed >> SecondShift)) * SecondMultiplier;
        return mixed ^ (mixed >> FinalShift);
    }
}
