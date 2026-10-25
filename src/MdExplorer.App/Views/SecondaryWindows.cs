using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Views.Graph;
using MdExplorer.App.Views.Help;
using MdExplorer.App.Views.Panels;
using MdExplorer.App.Views.Settings;
using MdExplorer.TagCloud.Views;

namespace MdExplorer.App.Views;

/// <summary>Die drei Bereiche, die das Hauptfenster in seine Spalten setzt.</summary>
/// <param name="Document">Dokumentbereich mit Vorschau und Editor.</param>
/// <param name="AllFiles">Liste aller Dateien.</param>
/// <param name="TagCloud">Tag-Cloud.</param>
internal sealed record MainWindowPanels(DocumentPanel Document, AllFilesPanel AllFiles, TagCloudPanel TagCloud);

/// <summary>Die Fabriken der Nebenfenster, wie der Container sie liefert.</summary>
/// <param name="Settings">Einstellungen.</param>
/// <param name="Graph">Graph-Ansicht.</param>
/// <param name="TagManagement">Tag-Verwaltung.</param>
/// <param name="Help">Handbuch.</param>
/// <param name="About">Info-Fenster.</param>
/// <param name="LogViewer">Protokoll.</param>
internal sealed record SecondaryWindowFactories(
    Func<SettingsWindow> Settings,
    Func<GraphWindow> Graph,
    Func<TagManagementWindow> TagManagement,
    Func<HelpWindow> Help,
    Func<AboutWindow> About,
    Func<LogViewerWindow> LogViewer);

/// <summary>
/// Öffnet die Nebenfenster des Hauptfensters. Handbuch und Protokoll gibt es höchstens einmal:
/// Ist eines offen, kommt es nach vorn, statt dass ein zweites daneben erscheint.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SecondaryWindows(Window owner, SecondaryWindowFactories factories)
{
    private HelpWindow? _help;
    private LogViewerWindow? _logViewer;

    /// <summary>Öffnet die Einstellungen als Dialog.</summary>
    public void ShowSettings() => _ = Owned(factories.Settings()).ShowDialog();

    /// <summary>Öffnet eine Graph-Ansicht; mehrere nebeneinander sind erlaubt.</summary>
    public void ShowGraph() => Owned(factories.Graph()).Show();

    /// <summary>Öffnet die Tag-Verwaltung als Dialog.</summary>
    public void ShowTagManagement() => _ = Owned(factories.TagManagement()).ShowDialog();

    /// <summary>Öffnet das Info-Fenster als Dialog.</summary>
    public void ShowAbout() => _ = Owned(factories.About()).ShowDialog();

    /// <summary>Zeigt das Protokoll.</summary>
    public void ShowLogViewer()
    {
        if (_logViewer is { } open)
        {
            BringForward(open);
            return;
        }
        LogViewerWindow window = Owned(factories.LogViewer());
        window.Closed += (_, _) => _logViewer = null;
        _logViewer = window;
        window.Show();
    }

    /// <summary>Zeigt das Handbuch beim Kapitel <paramref name="slug"/>.</summary>
    /// <param name="slug">Anker des Kapitels.</param>
    public async Task ShowHelpAsync(string slug)
    {
        if (_help is { } open)
        {
            BringForward(open);
            await open.NavigateToAsync(slug).ConfigureAwait(true);
            return;
        }
        HelpWindow window = Owned(factories.Help());
        window.Closed += (_, _) => _help = null;
        _help = window;
        window.Show();
        await window.NavigateToAsync(slug).ConfigureAwait(true);
    }

    private static void BringForward(Window open)
    {
        if (open.WindowState == WindowState.Minimized)
        {
            open.WindowState = WindowState.Normal;
        }
        _ = open.Activate();
    }

    private TWindow Owned<TWindow>(TWindow window)
        where TWindow : Window
    {
        window.Owner = owner;
        return window;
    }
}
