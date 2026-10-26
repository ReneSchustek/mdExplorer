namespace MdExplorer.App.Services;

/// <summary>
/// Die zuletzt geöffneten Dateien, jüngste zuerst, ohne Doppelte und höchstens
/// <see cref="Capacity"/> Einträge.
/// </summary>
/// <remarks>
/// Verglichen wird ohne Groß-/Kleinschreibung, wie Windows Pfade vergleicht: Zwei Pfade, die sich
/// nur darin unterscheiden, meinen dieselbe Datei und stehen nur einmal in der Liste, unter der
/// zuletzt verwendeten Schreibweise.
/// </remarks>
internal sealed class RecentFileList
{
    /// <summary>Höchstzahl der Einträge; was darüber hinausgeht, fällt am Ende weg.</summary>
    public const int Capacity = 15;

    private readonly List<string> _paths = [];

    /// <summary>Erzeugt die Liste aus einem gespeicherten Stand.</summary>
    /// <param name="paths">Die gespeicherten Pfade, jüngste zuerst; leere und doppelte werden übergangen.</param>
    public RecentFileList(IEnumerable<string>? paths)
    {
        foreach (string path in paths ?? [])
        {
            if (!string.IsNullOrWhiteSpace(path) && IndexOf(path) < 0 && _paths.Count < Capacity)
            {
                _paths.Add(path);
            }
        }
    }

    /// <summary>Die Pfade, jüngste zuerst.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>Setzt eine Datei an den Anfang.</summary>
    /// <param name="path">Der vollständige Pfad.</param>
    /// <returns><see langword="true"/>, wenn sich die Liste dadurch geändert hat.</returns>
    public bool Add(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int index = IndexOf(path);
        if (index == 0 && string.Equals(_paths[0], path, StringComparison.Ordinal))
        {
            return false;
        }
        if (index >= 0)
        {
            _paths.RemoveAt(index);
        }
        _paths.Insert(0, path);
        if (_paths.Count > Capacity)
        {
            _paths.RemoveAt(_paths.Count - 1);
        }
        return true;
    }

    /// <summary>Nimmt eine Datei aus der Liste.</summary>
    /// <param name="path">Der Pfad.</param>
    /// <returns><see langword="true"/>, wenn sie darin stand.</returns>
    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int index = IndexOf(path);
        if (index < 0)
        {
            return false;
        }
        _paths.RemoveAt(index);
        return true;
    }

    /// <summary>Leert die Liste.</summary>
    /// <returns><see langword="true"/>, wenn vorher etwas darin stand.</returns>
    public bool Clear()
    {
        if (_paths.Count == 0)
        {
            return false;
        }
        _paths.Clear();
        return true;
    }

    private int IndexOf(string path) => _paths.FindIndex(entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase));
}
