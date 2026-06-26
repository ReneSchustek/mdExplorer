using Microsoft.Win32;

namespace MdExplorer.App.Services;

/// <summary>
/// WPF-Implementierung von <see cref="IFileSaveDialogService"/> auf Basis von
/// <see cref="SaveFileDialog"/>. Ohne Owner-Window — die <c>ShowDialog()</c>-
/// Variante reicht für modale Speichern-Dialoge, die der jeweils aktive
/// Top-Level-Owner automatisch über die WPF-Fokus-Hierarchie erbt.
/// </summary>
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
