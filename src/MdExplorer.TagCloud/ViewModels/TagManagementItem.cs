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

/// <summary>Ein Tag-Eintrag im Tag-Management-Fenster.</summary>
/// <param name="Name">Original-Schreibweise des Tags.</param>
/// <param name="Slug">Eindeutiger Slug.</param>
/// <param name="Count">Anzahl Dateien mit diesem Tag.</param>
public sealed record TagManagementItem(string Name, string Slug, int Count);
