using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Services;

/// <summary>Ergebnis der Prüfung eines Pfads von außen.</summary>
/// <param name="AbsolutePath">Der normalisierte Pfad, wenn er geöffnet werden darf.</param>
/// <param name="RejectedValue">Was abgewiesen wurde, so weit es sich lesen ließ.</param>
/// <param name="Problem">Der Grund der Abweisung.</param>
internal sealed record MarkdownPathCheck(string? AbsolutePath, string? RejectedValue, MarkdownPathProblem? Problem)
{
    /// <summary>Kein Pfad übergeben; es gibt nichts zu öffnen und nichts zu melden.</summary>
    public static MarkdownPathCheck None { get; } = new(AbsolutePath: null, RejectedValue: null, Problem: null);

    /// <summary>Wahr, wenn weder ein Pfad noch eine Abweisung vorliegt.</summary>
    public bool IsNone => AbsolutePath is null && Problem is null;

    /// <summary>Ein Pfad, der geöffnet werden darf.</summary>
    /// <param name="absolutePath">Der normalisierte Pfad.</param>
    public static MarkdownPathCheck Accepted(string absolutePath) => new(absolutePath, RejectedValue: null, Problem: null);

    /// <summary>Ein abgewiesener Pfad.</summary>
    /// <param name="value">Was abgewiesen wurde.</param>
    /// <param name="problem">Warum.</param>
    public static MarkdownPathCheck Rejected(string value, MarkdownPathProblem problem) => new(AbsolutePath: null, value, problem);
}
