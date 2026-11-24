using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.Hosting;
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
