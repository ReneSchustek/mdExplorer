using System.Collections.Concurrent;
using MdExplorer.Core.Settings;

namespace MdExplorer.Indexer.Services;

/// <summary>
/// Liefert pro Verzeichnis die Glob-Muster aller <c>.mdignore</c>-Dateien
/// zwischen Root und Verzeichnis selbst (Eltern zuerst, dann das eigene).
/// Das Ergebnis wird thread-sicher gecacht — kein I/O bei zweitem Aufruf
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
    /// Liefert die kumulativ aggregierten Glob-Muster, die im angegebenen Verzeichnis
    /// gelten — beginnend mit Mustern aus <paramref name="rootAbsolutePath"/> bis hinunter
    /// zu <paramref name="directoryAbsolutePath"/>.
    /// </summary>
    public IReadOnlyList<string> GetPatternsFor(string rootAbsolutePath, string directoryAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAbsolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryAbsolutePath);

        string normalizedRoot = NormalizeDirectory(rootAbsolutePath);
        string normalizedDir = NormalizeDirectory(directoryAbsolutePath);

        if (!IsUnderRoot(normalizedDir, normalizedRoot))
        {
            return [];
        }

        List<string> chain = BuildChain(normalizedRoot, normalizedDir);
        List<string> aggregated = [];
        foreach (string dir in chain)
        {
            IReadOnlyList<string> patterns = GetCachedPatterns(dir);
            aggregated.AddRange(patterns);
        }
        return aggregated;
    }

    /// <summary>Verwirft alle gecachten <c>.mdignore</c>-Lesungen.</summary>
    public void Clear() => _cache.Clear();

    private IReadOnlyList<string> GetCachedPatterns(string dir)
    {
        if (_cache.TryGetValue(dir, out IReadOnlyList<string>? cached))
        {
            return cached;
        }
        IReadOnlyList<string> read = _reader.Read(dir);
        return _cache.GetOrAdd(dir, read);
    }

    private static bool IsUnderRoot(string directory, string root)
    {
        if (directory.Length < root.Length)
        {
            return false;
        }
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (directory.Length == root.Length)
        {
            return true;
        }
        char delimiter = directory[root.Length];
        return delimiter == Path.DirectorySeparatorChar || delimiter == Path.AltDirectorySeparatorChar;
    }

    private static string NormalizeDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static List<string> BuildChain(string root, string leaf)
    {
        List<string> chain = [];
        string current = leaf;
        while (true)
        {
            chain.Add(current);
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            string normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedParent.Length == current.Length)
            {
                break;
            }
            current = normalizedParent;
            if (current.Length < root.Length)
            {
                break;
            }
        }
        chain.Reverse();
        return chain;
    }
}
