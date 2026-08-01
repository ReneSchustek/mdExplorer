using System.Windows;
using MdExplorer.TagCloud.ViewModels;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.App.Services;

/// <summary>
/// WPF-Implementierung von <see cref="ITagManagementDialogService"/>. Zeigt eine
/// modale Bestätigung mit Ja/Nein-Buttons. Die UI-Abstraktion bleibt im
/// TagCloud-Modul (testbar), die WPF-Bindung lebt hier.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class TagManagementDialogService : ITagManagementDialogService
{
    /// <inheritdoc />
    public bool Confirm(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(message);
        MessageBoxResult result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
