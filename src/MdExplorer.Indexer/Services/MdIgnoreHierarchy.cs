using System.Collections.Concurrent;
using MdExplorer.Core.Settings;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Liefert pro Verzeichnis die <c>.mdignore</c>-Dateien zwischen Root und Verzeichnis selbst
/// (Eltern zuerst, dann das eigene), jede mit dem Ordner, in dem sie liegt.
/// Das Ergebnis wird thread-sicher gecacht, kein I/O bei zweitem Aufruf
/// für dasselbe Verzeichnis, bis <see cref="Clear"/> aufgerufen wird.
/// </summary>
public sealed class MdIgnoreHierarchy
{
    private readonly MdIgnoreReader _reader;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Erzeugt die Hierarchie und injiziert den <see cref="MdIgnoreReader"/>.</summary>
    public MdIgnoreHierarchy(MdIgnoreReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>
    /// Liefert die <c>.mdignore</c>-Dateien, die im angegebenen Verzeichnis gelten, beginnend
    /// bei <paramref name="rootAbsolutePath"/> bis hinunter zu <paramref name="directoryAbsolutePath"/>.
    /// Ordner ohne <c>.mdignore</c> fehlen in der Liste.
    /// </summary>
    public IReadOnlyList<MdIgnoreSource> GetSourcesFor(string rootAbsolutePath, string directoryAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryAbsolutePath);

        string normalizedRoot = NormalizeDirectory(rootAbsolutePath);
        string normalizedDir = NormalizeDirectory(directoryAbsolutePath);

        if (!PathScope.IsAtOrBelow(normalizedDir, normalizedRoot))
        {
            return [];
        }

        List<string> chain = BuildChain(normalizedRoot, normalizedDir);
        List<MdIgnoreSource> sources = [];
        foreach (string dir in chain)
        {
            IReadOnlyList<string> patterns = GetCachedPatterns(dir);
            if (patterns.Count > 0)
            {
                sources.Add(new MdIgnoreSource(dir, patterns));
            }
        }
        return sources;
    }

    /// <summary>Verwirft alle gecachten <c>.mdignore</c>-Lesungen.</summary>
    public void Clear() => _cache.Clear();

    private static string NormalizeDirectory(string path) => PathScope.TrimSeparators(Path.GetFullPath(path));

    private static List<string> BuildChain(string root, string leaf)
    {
        List<string> chain = [leaf];
        string current = leaf;
        while (!string.Equals(current, root, StringComparison.OrdinalIgnoreCase)
            && TryGetParent(current, out string parent)
            && parent.Length >= root.Length)
        {
            chain.Add(parent);
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    private static bool TryGetParent(string directory, out string parent)
    {
        parent = PathScope.TrimSeparators(Path.GetDirectoryName(directory) ?? string.Empty);

        // Wird der Pfad nach dem Kürzen nicht kürzer, steht die Suche an der Laufwerkswurzel und
        // liefe sonst endlos weiter.
        return parent.Length > 0 && parent.Length < directory.Length;
    }

    private IReadOnlyList<string> GetCachedPatterns(string dir)
    {
        if (_cache.TryGetValue(dir, out IReadOnlyList<string>? cached))
        {
            return cached;
        }
        // Lesen zwei Threads dasselbe Verzeichnis gleichzeitig, gewinnt der erste Eintrag. Beide haben
        // dieselbe Datei gelesen, der doppelte Zugriff schadet nicht.
        IReadOnlyList<string> read = _reader.Read(dir);
        return _cache.GetOrAdd(dir, read);
    }
}
