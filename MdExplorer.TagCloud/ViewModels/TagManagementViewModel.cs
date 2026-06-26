using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// ViewModel des Tag-Management-Fensters. Listet alle Tags mit Anzahl betroffener Dateien
/// und stellt projektweite Rename / Merge / Delete-Operationen bereit. Operationen verbergen
/// einen Bestaetigungsdialog ueber den injizierten <see cref="ITagManagementDialogService"/>.
/// </summary>
public sealed partial class TagManagementViewModel : ObservableObject
{
    private const int TopTagsLimit = 1_000;

    private readonly ITagStatisticsService _statisticsService;
    private readonly ITagManagementService _managementService;
    private readonly ITagManagementDialogService _dialogService;
    private readonly ILogger<TagManagementViewModel> _logger;
    private readonly object _itemsGate = new();

    [ObservableProperty]
    private TagManagementItem? _selectedItem;

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Erzeugt das ViewModel und bindet die Pflichtabhaengigkeiten.</summary>
    public TagManagementViewModel(
        ITagStatisticsService statisticsService,
        ITagManagementService managementService,
        ITagManagementDialogService dialogService,
        ILogger<TagManagementViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(statisticsService);
        ArgumentNullException.ThrowIfNull(managementService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(logger);

        _statisticsService = statisticsService;
        _managementService = managementService;
        _dialogService = dialogService;
        _logger = logger;
        Items = [];
        BindingOperations.EnableCollectionSynchronization(Items, _itemsGate);
    }

    /// <summary>Top-Tags mit Anzahl betroffener Dateien.</summary>
    public ObservableCollection<TagManagementItem> Items { get; }

    /// <summary>Laedt die Tag-Liste neu.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            int loaded = await ReloadItemsAsync(cancellationToken).ConfigureAwait(true);
            StatusMessage = string.Create(
                CultureInfo.CurrentCulture,
                $"{loaded} Tag(s) geladen.");
        }
        catch (OperationCanceledException)
        {
            // Refresh wurde abgebrochen — kein Fehler.
        }
        catch (DbException exception)
        {
            // SQLite-Spitze — Status setzen, Liste auf Vorstand lassen.
            LogRefreshFailed(_logger, exception);
            StatusMessage = "Tag-Liste konnte nicht geladen werden — siehe Log.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Reload als reiner Helfer ohne Status-Message. RunWriteAsync nutzt ihn,
    // damit das Op-Feedback nicht vom Refresh-Status ueberschrieben wird.
    private async Task<int> ReloadItemsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TagStatistic> stats = await _statisticsService
            .GetTopTagsAsync(TopTagsLimit, cancellationToken).ConfigureAwait(true);
        lock (_itemsGate)
        {
            Items.Clear();
            foreach (TagStatistic statistic in stats)
            {
                Items.Add(new TagManagementItem(statistic.Name, statistic.Slug, statistic.Count));
            }
        }
        return stats.Count;
    }

    /// <summary>Benennt den ausgewaehlten Tag um. Liest <see cref="NewTagName"/> als Zielname.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    public Task RenameAsync(CancellationToken cancellationToken) =>
        ExecuteWriteOperationAsync(
            "Tag umbenennen",
            (slug, ct) => _managementService.RenameAsync(slug, NewTagName, ct),
            cancellationToken);

    /// <summary>Fuehrt den ausgewaehlten Tag in <see cref="NewTagName"/> ueber.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    public Task MergeAsync(CancellationToken cancellationToken) =>
        ExecuteWriteOperationAsync(
            "Tags zusammenfuehren",
            (slug, ct) => _managementService.MergeAsync(slug, NewTagName, ct),
            cancellationToken);

    /// <summary>Loescht den ausgewaehlten Tag aus allen Dateien.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelectionWithoutTarget))]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }
        TagPreview preview = await _managementService
            .GetPreviewAsync(SelectedItem.Slug, cancellationToken).ConfigureAwait(true);
        string question = BuildConfirmText("loeschen", SelectedItem.Slug, null, preview);
        if (!_dialogService.Confirm("Tag loeschen", question))
        {
            return;
        }
        await RunWriteAsync(
            "Tag loeschen",
            ct => _managementService.DeleteAsync(SelectedItem.Slug, ct),
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(TagManagementItem? value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewTagNameChanged(string value)
    {
        RenameCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
    }

    private bool CanOperateOnSelection() =>
        !IsBusy && SelectedItem is not null && !string.IsNullOrWhiteSpace(NewTagName);

    private bool CanOperateOnSelectionWithoutTarget() =>
        !IsBusy && SelectedItem is not null;

    private async Task ExecuteWriteOperationAsync(
        string title,
        Func<string, CancellationToken, Task<TagRewriteResult>> operation,
        CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }
        TagPreview preview = await _managementService
            .GetPreviewAsync(SelectedItem.Slug, cancellationToken).ConfigureAwait(true);
        string question = BuildConfirmText(title, SelectedItem.Slug, NewTagName, preview);
        if (!_dialogService.Confirm(title, question))
        {
            return;
        }
        await RunWriteAsync(title, ct => operation(SelectedItem.Slug, ct), cancellationToken).ConfigureAwait(true);
    }

    private async Task RunWriteAsync(
        string title,
        Func<CancellationToken, Task<TagRewriteResult>> action,
        CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            TagRewriteResult result = await action(cancellationToken).ConfigureAwait(true);
            LogOperationCompleted(_logger, title, result.Slug, result.FilesAffected, result.FilesAttempted, result.Errors.Count);
            NewTagName = string.Empty;
            try
            {
                _ = await ReloadItemsAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (DbException reloadException)
            {
                // Reload-Fehler darf das Op-Feedback nicht verschlucken — nur loggen.
                LogRefreshFailed(_logger, reloadException);
            }
            StatusMessage = string.Create(
                CultureInfo.CurrentCulture,
                $"{title}: {result.FilesAffected} von {result.FilesAttempted} Datei(en) geaendert. Fehler: {result.Errors.Count}.");
        }
        catch (OperationCanceledException)
        {
            // Operation wurde abgebrochen — kein Fehler.
        }
        catch (DbException exception)
        {
            // SQLite-Spitze beim Preview-Lookup oder Schreibvorgang.
            LogRefreshFailed(_logger, exception);
            StatusMessage = string.Create(
                CultureInfo.CurrentCulture,
                $"{title} fehlgeschlagen — siehe Log.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildConfirmText(string title, string slug, string? targetName, TagPreview preview)
    {
        string head = targetName is null
            ? string.Create(CultureInfo.CurrentCulture, $"{title}: '#{slug}'")
            : string.Create(CultureInfo.CurrentCulture, $"{title}: '#{slug}' -> '#{targetName.TrimStart('#')}'");
        if (preview.FileCount == 0)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{head}\n\nKeine Dateien betroffen.");
        }
        string sampleBlock = preview.SamplePaths.Count == 0
            ? string.Empty
            : "\n - " + string.Join("\n - ", preview.SamplePaths);
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{head}\n\n{preview.FileCount} Datei(en) betroffen.{sampleBlock}\n\nFortfahren?");
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "{Operation} (Slug={Slug}): {Affected}/{Attempted} Dateien, {ErrorCount} Fehler.")]
    private static partial void LogOperationCompleted(ILogger logger, string operation, string slug, int affected, int attempted, int errorCount);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Tag-Liste konnte nicht geladen werden — Datenbank-Spitze.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);
}

/// <summary>Ein Tag-Eintrag im Tag-Management-Fenster.</summary>
/// <param name="Name">Original-Schreibweise des Tags.</param>
/// <param name="Slug">Eindeutiger Slug.</param>
/// <param name="Count">Anzahl Dateien mit diesem Tag.</param>
public sealed record TagManagementItem(string Name, string Slug, int Count);

/// <summary>UI-Abstraktion fuer Tag-Management-Bestaetigungen.</summary>
public interface ITagManagementDialogService
{
    /// <summary>Zeigt eine Bestaetigung und liefert die Antwort des Benutzers.</summary>
    bool Confirm(string title, string message);
}
