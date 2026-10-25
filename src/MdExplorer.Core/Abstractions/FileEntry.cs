namespace MdExplorer.Core.Abstractions;

/// <summary>Eine Datei, wie die Aufzählung ihres Verzeichnisses sie liefert.</summary>
/// <param name="Path">Vollständiger Pfad.</param>
/// <param name="SizeBytes">Größe in Byte.</param>
/// <param name="LastWriteTimeUtc">Letzte Schreibzeit in UTC.</param>
/// <param name="IsReparsePoint">Ob die Datei ein Reparse-Point ist (Symlink, Cloud-Platzhalter).</param>
public readonly record struct FileEntry(string Path, long SizeBytes, DateTime LastWriteTimeUtc, bool IsReparsePoint);
