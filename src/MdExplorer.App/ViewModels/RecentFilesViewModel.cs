using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Der Verlauf der zuletzt geöffneten Dateien: Knopf „Zuletzt geöffnet“ in der Kopfleiste und die
/// Sprungliste am Taskleistensymbol.
/// </summary>
/// <remarks>
/// <para>
/// Geöffnet wird nicht hier, sondern über <see cref="OpenRequested"/> beim Hauptfenster. So nimmt
/// ein Eintrag denselben Weg wie ein Klick in der Liste, samt Rückfrage zu ungespeicherten
/// Änderungen und dem Direktladen von Dateien außerhalb des Index.
/// </para>
/// <para>
/// Der Verlauf steht in <c>ui-layout.json</c> neben den Fensterzuständen, nicht in der Datenbank:
/// Er gehört zum Profil des Nutzers und enthält auch Dateien, die nie indiziert waren. Eine Datei,
/// die es nicht mehr gibt, bleibt erkennbar stehen, bis der Nutzer sie anklickt; dann wird sie
/// entfernt, statt im Hintergrund still zu verschwinden.
/// </para>
/// </remarks>
internal sealed partial class RecentFilesViewModel : ObservableObject
{
    private readonly UiSettingsStore _settingsStore;
    private readonly IFileSystem _fileSystem;
    private readonly IRecentFilesJumpList _jumpList;
    private readonly ILogger<RecentFilesViewModel> _logger;
    private readonly RecentFileList _list;

    /// <summary>Erzeugt den Verlauf aus dem gespeicherten Stand.</summary>
    /// <param name="settingsStore">Speichert den Verlauf mit dem Layout.</param>
    /// <param name="fileSystem">Prüft, ob eine Datei noch existiert.</param>
    /// <param name="jumpList">Die Sprungliste des Taskleistensymbols.</param>
    /// <param name="logger">Protokoll.</param>
    public RecentFilesViewModel(UiSettingsStore settingsStore, IFileSystem fileSystem, IRecentFilesJumpList jumpList, ILogger<RecentFilesViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(jumpList);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsStore = settingsStore;
        _fileSystem = fileSystem;
        _jumpList = jumpList;
        _logger = logger;
        _list = new RecentFileList(settingsStore.Load().RecentFiles);
        Refresh();
    }

    /// <summary>Ein Eintrag soll geöffnet werden; der Pfad ist vollständig.</summary>
    public event Action<string>? OpenRequested;

    /// <summary>Die Einträge, jüngste zuerst, mit dem Stand der letzten Prüfung auf der Platte.</summary>
    public ObservableCollection<RecentFileItem> Items { get; } = [];

    /// <summary>Wahr, wenn der Verlauf Einträge hat.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>Nimmt eine geöffnete Datei an den Anfang des Verlaufs.</summary>
    /// <param name="absolutePath">Der vollständige Pfad.</param>
    public void Record(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (_list.Add(absolutePath))
        {
            Persist();
        }
    }

    /// <summary>Prüft jeden Eintrag erneut auf der Platte, etwa bevor die Liste aufklappt.</summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (string path in _list.Paths)
        {
            Items.Add(new RecentFileItem(path, Path.GetFileName(path), Path.GetDirectoryName(path) ?? string.Empty, _fileSystem.FileExists(path)));
        }
        OnPropertyChanged(nameof(HasItems));
        ClearCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 1741, Level = LogLevel.Information, Message = "Eintrag {Path} aus dem Verlauf entfernt; die Datei gibt es nicht mehr.")]
    private static partial void LogMissingRemoved(ILogger logger, string path);

    /// <summary>Öffnet einen Eintrag; fehlt die Datei, verschwindet er stattdessen aus dem Verlauf.</summary>
    /// <param name="item">Der Eintrag.</param>
    [RelayCommand]
    private void Open(RecentFileItem? item)
    {
        if (item is null)
        {
            return;
        }
        if (!_fileSystem.FileExists(item.FullPath))
        {
            LogMissingRemoved(_logger, item.FullPath);
            Remove(item);
            return;
        }
        OpenRequested?.Invoke(item.FullPath);
    }

    /// <summary>Nimmt einen Eintrag aus dem Verlauf.</summary>
    /// <param name="item">Der Eintrag.</param>
    [RelayCommand]
    private void Remove(RecentFileItem? item)
    {
        if (item is not null && _list.Remove(item.FullPath))
        {
            Persist();
        }
    }

    /// <summary>Leert den Verlauf, in der Anwendung und in der Sprungliste.</summary>
    [RelayCommand(CanExecute = nameof(HasItems))]
    private void Clear()
    {
        if (_list.Clear())
        {
            Persist();
        }
    }

    private void Persist()
    {
        // Fortschreiben statt neu anlegen: Spaltenbreiten, Hilfe- und Graph-Fenster stehen in
        // derselben Datei.
        _settingsStore.Save(_settingsStore.Load() with { RecentFiles = [.. _list.Paths] });
        _jumpList.Show(_list.Paths);
        Refresh();
    }
}

/// <summary>Ein Eintrag im Verlauf.</summary>
/// <param name="FullPath">Der vollständige Pfad.</param>
/// <param name="Name">Der Dateiname.</param>
/// <param name="Folder">Der Ordner der Datei.</param>
/// <param name="Exists">Ob die Datei bei der letzten Prüfung noch da war.</param>
internal sealed record RecentFileItem(string FullPath, string Name, string Folder, bool Exists);
