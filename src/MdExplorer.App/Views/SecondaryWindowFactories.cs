using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Hosting;
using MdExplorer.App.Views.Graph;
using MdExplorer.App.Views.Help;
using MdExplorer.App.Views.Panels;
using MdExplorer.App.Views.Settings;
using MdExplorer.TagCloud.Views;

namespace MdExplorer.App.Views;

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
