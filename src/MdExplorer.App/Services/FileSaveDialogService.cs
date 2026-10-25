using Microsoft.Win32;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.App.Services;

/// <summary>
/// WPF-Implementierung von <see cref="IFileSaveDialogService"/> auf Basis von
/// <see cref="SaveFileDialog"/>. Ohne Besitzerfenster: <c>ShowDialog()</c> ohne Argument
/// nimmt das gerade aktive Fenster als Besitzer, und das ist immer das, aus dem gespeichert wird.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class FileSaveDialogService : IFileSaveDialogService
{
    /// <inheritdoc />
    public string? PromptForSavePath(string defaultFileName, string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);

        SaveFileDialog dialog = new()
        {
            FileName = defaultFileName,
            Filter = filter,
            OverwritePrompt = true,
            AddExtension = true,
        };
        bool? confirmed = dialog.ShowDialog();
        return confirmed == true ? dialog.FileName : null;
    }
}
