using System.IO;
using System.Windows;
using MdExplorer.Core.Abstractions;
using Microsoft.Win32;

namespace MdExplorer.App.Services;

/// <summary>
/// WPF-Implementierung von <see cref="IDialogService"/>. Nutzt
/// <see cref="OpenFolderDialog"/> aus <c>Microsoft.Win32</c> (ab .NET 8 in WPF integriert)
/// für die Ordner-Auswahl und <see cref="MessageBox"/> für Fehler-/Bestätigungs-Dialoge.
/// </summary>
internal sealed class DialogService : IDialogService
{
    /// <inheritdoc />
    public string? PickDirectory(string title, string? initialDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        OpenFolderDialog dialog = new()
        {
            Title = title,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }
        bool? result = dialog.ShowDialog();
        return result == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _ = MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <inheritdoc />
    public bool Confirm(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        MessageBoxResult result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }
}
