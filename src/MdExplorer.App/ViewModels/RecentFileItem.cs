using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Ein Eintrag im Verlauf.</summary>
/// <param name="FullPath">Der vollständige Pfad.</param>
/// <param name="Name">Der Dateiname.</param>
/// <param name="Folder">Der Ordner der Datei.</param>
/// <param name="Exists">Ob die Datei bei der letzten Prüfung noch da war.</param>
internal sealed record RecentFileItem(string FullPath, string Name, string Folder, bool Exists);
