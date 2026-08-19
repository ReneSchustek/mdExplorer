namespace MdExplorer.Core.Abstractions;

/// <summary>
/// Hält die Zahl der aktuell nicht verarbeitbaren Dateien fest und gibt sie an die
/// Betriebsanzeige weiter. Der Parser meldet den Stand nach jedem Durchlauf, die
/// Oberfläche liest ihn und lauscht auf <see cref="Changed"/>.
/// </summary>
public interface IParseFailureStatus
{
    /// <summary>Zahl der Dateien, die mit ihrem aktuellen Inhalt nicht geparst werden können.</summary>
    int UnparsableFileCount { get; }

    /// <summary>Wird gefeuert, sobald sich <see cref="UnparsableFileCount"/> tatsächlich ändert.</summary>
    event EventHandler? Changed;

    /// <summary>Übernimmt den Stand eines abgeschlossenen Parser-Durchlaufs.</summary>
    /// <param name="unparsableFileCount">Zahl der Dateien mit gültigem Fehlschlag-Vermerk.</param>
    void Update(int unparsableFileCount);
}
