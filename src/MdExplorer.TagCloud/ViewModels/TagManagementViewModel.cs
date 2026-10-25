using System.Collections.ObjectModel;
using System.Data.Common;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.Parser.Services;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.Localization;
using MdExplorer.TagCloud.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.TagCloud.ViewModels;

/// <summary>
/// ViewModel des Tag-Management-Fensters. Listet alle Tags mit Anzahl betroffener Dateien
/// und stellt projektweite Rename / Merge / Delete-Operationen bereit. Vor jeder Operation fragt
/// es über den injizierten <see cref="ITagManagementDialogService"/> nach und zeigt dabei die Vorschau.
/// </summary>
public sealed partial class TagManagementViewModel : ObservableObject
{
    // Seltenere Schlagworte jenseits dieser Grenze erscheinen in der Liste nicht und lassen sich
    // hier nicht bearbeiten. Der Wert ist gesetzt, nicht gemessen.
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
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isBusy;

    /// <summary>Erzeugt das ViewModel und bindet die Pflichtabhängigkeiten.</summary>
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

    /// <summary>Lädt die Tag-Liste neu.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            int loaded = await ReloadItemsAsync(cancellationToken).ConfigureAwait(true);
            StatusMessage = UiText.Format("TagManagement_Status_Loaded", loaded);
        }
        catch (OperationCanceledException)
        {
            // Refresh wurde abgebrochen, kein Fehler.
        }
        catch (DbException exception)
        {
            // Eine SQLite-Spitze: Die Liste bleibt auf dem vorigen Stand, der Status sagt es.
            LogRefreshFailed(_logger, exception);
            StatusMessage = UiText.Get("TagManagement_Status_LoadFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Benennt den ausgewählten Tag um. Liest <see cref="NewTagName"/> als Zielname.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    public Task RenameAsync(CancellationToken cancellationToken) =>
        ExecuteWriteOperationAsync(
            UiText.Get("TagManagement_Operation_Rename"),
            (slug, ct) => _managementService.RenameAsync(slug, NewTagName, ct),
            cancellationToken);

    /// <summary>Führt den ausgewählten Tag in <see cref="NewTagName"/> über.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelection))]
    public Task MergeAsync(CancellationToken cancellationToken) =>
        ExecuteWriteOperationAsync(
            UiText.Get("TagManagement_Operation_Merge"),
            (slug, ct) => _managementService.MergeAsync(slug, NewTagName, ct),
            cancellationToken);

    /// <summary>Löscht den ausgewählten Tag aus allen Dateien.</summary>
    [RelayCommand(CanExecute = nameof(CanOperateOnSelectionWithoutTarget))]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        string deleteTitle = UiText.Get("TagManagement_Operation_Delete");
        if (SelectedItem is null || await TryGetPreviewAsync(deleteTitle, SelectedItem.Slug, cancellationToken).ConfigureAwait(true) is not { } preview)
        {
            return;
        }
        string question = BuildConfirmText(deleteTitle, SelectedItem.Slug, null, preview);
        if (!_dialogService.Confirm(deleteTitle, question))
        {
            return;
        }
        await RunWriteAsync(
            deleteTitle,
            ct => _managementService.DeleteAsync(SelectedItem.Slug, ct),
            cancellationToken).ConfigureAwait(true);
    }

    private static bool IsValidTagName(string name) => HashtagPattern.IsValidName(name.Trim());

    private static string BuildConfirmText(string title, string slug, string? targetName, TagPreview preview)
    {
        string head = targetName is null
            ? UiText.Format("TagManagement_Confirm_Head", title, slug)
            : UiText.Format("TagManagement_Confirm_HeadWithTarget", title, slug, targetName.TrimStart('#'));
        if (preview.FileCount == 0)
        {
            return head + "\n\n" + UiText.Get("TagManagement_Confirm_NoFiles");
        }
        string sampleBlock = preview.SamplePaths.Count == 0
            ? string.Empty
            : "\n - " + string.Join("\n - ", preview.SamplePaths);
        return head + "\n\n" + UiText.Format("TagManagement_Confirm_FileCount", preview.FileCount)
            + sampleBlock + "\n\n" + UiText.Get("TagManagement_Confirm_Proceed");
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "{Operation} (Slug={Slug}): {Affected}/{Attempted} Dateien, {ErrorCount} Fehler.")]
    private static partial void LogOperationCompleted(ILogger logger, string operation, string slug, int affected, int attempted, int errorCount);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Tag-Liste konnte nicht geladen werden — Datenbank-Spitze.")]
    private static partial void LogRefreshFailed(ILogger logger, Exception exception);

    // Lädt neu, ohne die Statuszeile anzufassen. RunWriteAsync braucht das, damit die Meldung
    // zur Operation nicht von der Lade-Meldung überschrieben wird.
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
        if (!string.IsNullOrWhiteSpace(value) && !IsValidTagName(value))
        {
            StatusMessage = UiText.Format("TagManagement_Status_InvalidName", value.Trim());
        }
    }

    private bool CanOperateOnSelection() =>
        !IsBusy && SelectedItem is not null && !string.IsNullOrWhiteSpace(NewTagName) && IsValidTagName(NewTagName);

    private bool CanOperateOnSelectionWithoutTarget() =>
        !IsBusy && SelectedItem is not null;

    private async Task ExecuteWriteOperationAsync(
        string title,
        Func<string, CancellationToken, Task<TagRewriteResult>> operation,
        CancellationToken cancellationToken)
    {
        if (SelectedItem is null || await TryGetPreviewAsync(title, SelectedItem.Slug, cancellationToken).ConfigureAwait(true) is not { } preview)
        {
            return;
        }
        string question = BuildConfirmText(title, SelectedItem.Slug, NewTagName, preview);
        if (!_dialogService.Confirm(title, question))
        {
            return;
        }
        await RunWriteAsync(title, ct => operation(SelectedItem.Slug, ct), cancellationToken).ConfigureAwait(true);
    }

    // Scheitert nur das Neuladen nach einer Operation, bleibt die Meldung zur Operation stehen.
    private async Task TryReloadItemsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await ReloadItemsAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (DbException reloadException)
        {
            LogRefreshFailed(_logger, reloadException);
        }
    }

    // Die Vorschau liest die Datenbank; eine Spitze dort endet als Meldung, nicht als Ausnahme
    // aus dem Befehl.
    private async Task<TagPreview?> TryGetPreviewAsync(string title, string slug, CancellationToken cancellationToken)
    {
        try
        {
            return await _managementService.GetPreviewAsync(slug, cancellationToken).ConfigureAwait(true);
        }
        catch (DbException exception)
        {
            LogRefreshFailed(_logger, exception);
            StatusMessage = UiText.Format("TagManagement_Status_Failed", title);
            return null;
        }
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
            await TryReloadItemsAsync(cancellationToken).ConfigureAwait(true);
            StatusMessage = UiText.Format(
                "TagManagement_Status_Completed", title, result.FilesAffected, result.FilesAttempted, result.Errors.Count);
        }
        catch (OperationCanceledException)
        {
            // Operation wurde abgebrochen; kein Fehler.
        }
        catch (DbException exception)
        {
            // Datenbank-Spitze beim Lesen der betroffenen Dateien.
            LogRefreshFailed(_logger, exception);
            StatusMessage = UiText.Format("TagManagement_Status_Failed", title);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Ein Tag-Eintrag im Tag-Management-Fenster.</summary>
/// <param name="Name">Original-Schreibweise des Tags.</param>
/// <param name="Slug">Eindeutiger Slug.</param>
/// <param name="Count">Anzahl Dateien mit diesem Tag.</param>
public sealed record TagManagementItem(string Name, string Slug, int Count);

/// <summary>UI-Abstraktion für Tag-Management-Bestätigungen.</summary>
public interface ITagManagementDialogService
{
    /// <summary>Zeigt eine Bestätigung und liefert die Antwort des Benutzers.</summary>
    bool Confirm(string title, string message);
}
