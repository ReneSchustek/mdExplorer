using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.ViewModels.Settings;

/// <summary>
/// ViewModel für den Tab „Indexierung" — verwaltet die Roots-Liste und die
/// Ausschluss-Muster-Liste. Beide Listen sind <see cref="ObservableCollection{T}"/>,
/// damit die UI automatisch mitzieht.
/// </summary>
internal sealed partial class IndexingTabViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    // Die per Folder-Tree-Kontextmenue gesetzten UI-Ausschluesse werden vom Settings-Dialog
    // nicht editiert, muessen aber beim Save unveraendert durchgereicht werden, damit ein
    // OK-Klick im Dialog die im Tree gesetzten Pausen nicht stillschweigend verwirft.
    private readonly IReadOnlyList<string> _preservedUiExcludedFolders;

    [ObservableProperty]
    private string? _selectedRoot;

    [ObservableProperty]
    private string? _selectedExclusion;

    [ObservableProperty]
    private string _newExclusionPattern = string.Empty;

    [ObservableProperty]
    private bool _autoExtractHashtags;

    /// <summary>Erzeugt das ViewModel auf Basis der aktuellen Settings.</summary>
    public IndexingTabViewModel(IndexingSettings initial, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(dialogService);

        _dialogService = dialogService;
        Roots = [.. initial.Roots];
        ExclusionPatterns = [.. initial.ExclusionPatterns];
        _preservedUiExcludedFolders = initial.UiExcludedFolders;
        _autoExtractHashtags = initial.AutoExtractHashtags;

        AddRootCommand = new RelayCommand(AddRoot);
        RemoveRootCommand = new RelayCommand(RemoveRoot, () => SelectedRoot is not null);
        AddExclusionCommand = new RelayCommand(AddExclusion, () => !string.IsNullOrWhiteSpace(NewExclusionPattern));
        RemoveExclusionCommand = new RelayCommand(RemoveExclusion, () => SelectedExclusion is not null);
    }

    /// <summary>Editierbare Liste der Index-Roots.</summary>
    public ObservableCollection<string> Roots { get; }

    /// <summary>Editierbare Liste der Glob-Ausschluss-Muster (mit <c>!</c>-Negation).</summary>
    public ObservableCollection<string> ExclusionPatterns { get; }

    /// <summary>Öffnet den Ordner-Auswahl-Dialog und nimmt das Ergebnis als neuen Root auf.</summary>
    public RelayCommand AddRootCommand { get; }

    /// <summary>Entfernt den aktuell selektierten Root aus der Liste.</summary>
    public RelayCommand RemoveRootCommand { get; }

    /// <summary>Übernimmt den Inhalt von <see cref="NewExclusionPattern"/> als neues Muster.</summary>
    public RelayCommand AddExclusionCommand { get; }

    /// <summary>Entfernt das selektierte Ausschluss-Muster.</summary>
    public RelayCommand RemoveExclusionCommand { get; }

    /// <summary>Baut die aktuelle Eingabe in einen <see cref="IndexingSettings"/>-Record zusammen.</summary>
    public IndexingSettings ToSettings() => new([.. Roots], [.. ExclusionPatterns], _preservedUiExcludedFolders, AutoExtractHashtags);

    partial void OnSelectedRootChanged(string? value) => RemoveRootCommand.NotifyCanExecuteChanged();

    partial void OnSelectedExclusionChanged(string? value) => RemoveExclusionCommand.NotifyCanExecuteChanged();

    partial void OnNewExclusionPatternChanged(string value) => AddExclusionCommand.NotifyCanExecuteChanged();

    private void AddRoot()
    {
        string? picked = _dialogService.PickDirectory("Index-Root wählen", null);
        if (picked is null)
        {
            return;
        }
        if (Roots.Contains(picked, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        Roots.Add(picked);
    }

    private void RemoveRoot()
    {
        if (SelectedRoot is null)
        {
            return;
        }
        _ = Roots.Remove(SelectedRoot);
        SelectedRoot = null;
    }

    private void AddExclusion()
    {
        string pattern = NewExclusionPattern.Trim();
        if (pattern.Length == 0 || ExclusionPatterns.Contains(pattern, StringComparer.Ordinal))
        {
            return;
        }
        ExclusionPatterns.Add(pattern);
        NewExclusionPattern = string.Empty;
    }

    private void RemoveExclusion()
    {
        if (SelectedExclusion is null)
        {
            return;
        }
        _ = ExclusionPatterns.Remove(SelectedExclusion);
        SelectedExclusion = null;
    }
}
