namespace MdExplorer.App.ViewModels;

/// <summary>
/// Schaltzustaende des <see cref="DocumentPanelViewModel"/>. <c>Read</c> zeigt die WebView2-Preview,
/// <c>Edit</c> blendet den Markdown-Editor ein.
/// </summary>
internal enum DocumentPanelMode
{
    /// <summary>Lesemodus mit gerendertem HTML in der WebView2.</summary>
    Read,

    /// <summary>Bearbeiten-Modus mit monospace TextBox.</summary>
    Edit,
}
