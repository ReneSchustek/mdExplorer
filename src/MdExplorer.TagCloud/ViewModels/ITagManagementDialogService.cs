using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Diagnostics;
using MdExplorer.Parser.Services;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Localization;
using MdExplorer.TagCloud.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>UI-Abstraktion für Tag-Management-Bestätigungen.</summary>
public interface ITagManagementDialogService
{
    /// <summary>Zeigt eine Bestätigung und liefert die Antwort des Benutzers.</summary>
    bool Confirm(string title, string message);
}
