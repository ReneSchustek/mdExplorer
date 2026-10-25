using MdExplorer.App.Logging;
using MdExplorer.App.Services;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels;
using MdExplorer.App.ViewModels.Help;
using MdExplorer.App.ViewModels.Settings;
using MdExplorer.App.Views;
using MdExplorer.App.Views.Help;
using MdExplorer.App.Views.Panels;
using MdExplorer.App.Views.Settings;
using MdExplorer.Core;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.DependencyInjection;
using MdExplorer.Core.Settings;
using MdExplorer.Data;
using MdExplorer.Data.Options;
using MdExplorer.App.ViewModels.Graph;
using MdExplorer.App.Views.Graph;
using MdExplorer.Graph.DependencyInjection;
using MdExplorer.Graph.Options;
using MdExplorer.Graph.Services;
using MdExplorer.Indexer.Abstractions;
using MdExplorer.Indexer.DependencyInjection;
using MdExplorer.Indexer.Options;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.DependencyInjection;
using MdExplorer.Parser.Options;
using MdExplorer.Search.DependencyInjection;
using MdExplorer.Search.Options;
using MdExplorer.TagCloud.Abstractions;
using MdExplorer.TagCloud.DependencyInjection;
using MdExplorer.TagCloud.Options;
using MdExplorer.TagCloud.ViewModels;
using MdExplorer.TagCloud.Views;
using MdExplorer.Update.DependencyInjection;
using MdExplorer.Update.Options;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Registrierungen der Darstellungsschicht: Dienste und ViewModels, Bereiche und Fenster. Reine
/// Registrierungslisten; die Optionen und Module der Schichten darunter registriert
/// <see cref="AppHostBuilder"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class PresentationRegistrations
{
    /// <summary>Dienste der Oberfläche und die ViewModels.</summary>
    /// <param name="services">Container der Anwendung.</param>
    public static IServiceCollection AddAppViewModels(this IServiceCollection services)
    {
        _ = services.AddSingleton<ISystemThemeProvider>(sp => new SystemThemeProvider());
        _ = services.AddSingleton<IEffectiveThemeProvider>(sp => new EffectiveThemeProvider(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ISystemThemeProvider>()));
        _ = services.AddSingleton(sp => new PreviewHtmlBuilder(
            sp.GetRequiredService<IEffectiveThemeProvider>(),
            sp.GetRequiredService<ISettingsService>()));
        _ = services.AddSingleton(sp => new ThemeApplier(sp.GetRequiredService<IEffectiveThemeProvider>()));
        _ = services.AddSingleton(sp => new UiSettingsStore(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ILogger<UiSettingsStore>>()));
        _ = services.AddSingleton<IDocumentLocator>(sp =>
            new MarkdownFileDocumentLocator(sp.GetRequiredService<IServiceScopeFactory>()));
        _ = services.AddSingleton<IDialogService, DialogService>();
        _ = services.AddSingleton<IExternalLinkLauncher, ShellExternalLinkLauncher>();
        _ = services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        _ = services.AddSingleton<IFileSaveDialogService, FileSaveDialogService>();
        // Dieselbe Instanz, in die Serilog schreibt; eine zweite bliebe leer.
        _ = services.AddSingleton<IMemoryLogStore>(_ => MemorySink.Instance);
        _ = services.AddSingleton<IOperationHealthProvider>(sp => new OperationHealthProvider(
            sp.GetRequiredService<IMemoryLogStore>(),
            sp.GetRequiredService<IParseFailureStatus>(),
            sp.GetRequiredService<IBackgroundServiceStatus>()));

        _ = services.AddSingleton(sp => new FolderTreeViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<ILogger<FolderTreeViewModel>>(),
            sp.GetRequiredService<IExclusionFilter>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        _ = services.AddSingleton(sp => new AllFilesViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<AllFilesViewModel>>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        _ = services.AddSingleton(sp => new SearchViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<ILogger<SearchViewModel>>()));
        _ = services.AddSingleton(sp => new TagCloudViewModel(
            new ScopedTagStatisticsService(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IOptions<TagCloudOptions>>(),
            sp.GetRequiredService<ILogger<TagCloudViewModel>>()));
        _ = services.AddSingleton(sp => new PreviewViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<PreviewHtmlBuilder>(),
            sp.GetRequiredService<ILogger<PreviewViewModel>>()));
        _ = services.AddSingleton<IEditorConfirmationDialogService, EditorConfirmationDialogService>();
        _ = services.AddSingleton(sp => new MarkdownEditorViewModel(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ITagExtractor>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IEditorConfirmationDialogService>(),
            sp.GetRequiredService<ILogger<MarkdownEditorViewModel>>()));
        _ = services.AddSingleton<IUnsavedChangesDialogService, UnsavedChangesDialogService>();
        _ = services.AddSingleton<IUnsavedChangesGuard>(sp => new UnsavedChangesGuard(
            sp.GetRequiredService<MarkdownEditorViewModel>(),
            sp.GetRequiredService<IUnsavedChangesDialogService>()));
        _ = services.AddSingleton<IDocumentFileService>(sp => new DocumentFileService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ILogger<DocumentFileService>>()));
        _ = services.AddSingleton(sp => new DocumentRelationsViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IDocumentFileService>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IUnsavedChangesGuard>(),
            sp.GetRequiredService<ILogger<DocumentRelationsViewModel>>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        _ = services.AddSingleton(sp => new DocumentPanelViewModel(
            sp.GetRequiredService<PreviewViewModel>(),
            sp.GetRequiredService<MarkdownEditorViewModel>(),
            sp.GetRequiredService<DocumentRelationsViewModel>(),
            sp.GetRequiredService<IMarkdownParser>(),
            sp.GetRequiredService<IDocumentLocator>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IUnsavedChangesGuard>(),
            sp.GetRequiredService<ILogger<DocumentPanelViewModel>>()));
        _ = services.AddSingleton(sp => new MainViewModel(
            new MainPanels(
                sp.GetRequiredService<FolderTreeViewModel>(),
                sp.GetRequiredService<AllFilesViewModel>(),
                sp.GetRequiredService<SearchViewModel>(),
                sp.GetRequiredService<DocumentPanelViewModel>(),
                sp.GetRequiredService<TagCloudViewModel>()),
            new MainServices(
                sp.GetRequiredService<IDocumentLocator>(),
                sp.GetRequiredService<UiSettingsStore>(),
                sp.GetRequiredService<IOperationHealthProvider>(),
                sp.GetRequiredService<IUiDispatcher>(),
                sp.GetRequiredService<MdExplorer.Indexer.Abstractions.IIndexer>(),
                sp.GetRequiredService<IMessenger>(),
                sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        // Navigiert wird über das eine MainViewModel, das die Panels kennt.
        _ = services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());
        _ = services.AddSingleton(sp => new ExternalFileOpener(
            sp.GetRequiredService<MainViewModel>().OpenFileAsync,
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<ILogger<ExternalFileOpener>>()));

        return services;
    }

    /// <summary>Bereiche, Fenster und die Fabriken, über die das Hauptfenster sie öffnet.</summary>
    /// <param name="services">Container der Anwendung.</param>
    public static IServiceCollection AddAppWindows(this IServiceCollection services)
    {
        _ = services.AddSingleton(sp => new PreviewPanel(
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<IExternalLinkLauncher>(),
            sp.GetRequiredService<ILogger<PreviewPanel>>()));
        _ = services.AddSingleton(sp => new DocumentPanel(sp.GetRequiredService<PreviewPanel>()));
        _ = services.AddSingleton(sp => new AllFilesPanel());
        _ = services.AddSingleton(sp => new TagCloudPanel());

        _ = services.AddSingleton(sp => new SplashViewModel());
        _ = services.AddTransient(sp => new SettingsWindowViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<SettingsValidator>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<MdExplorer.Update.Abstractions.IUpdateChecker>(),
            sp.GetRequiredService<MdExplorer.Update.Abstractions.IUpdateInstaller>(),
            sp.GetRequiredService<ILogger<SettingsWindowViewModel>>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        _ = services.AddTransient(sp => new SettingsWindow(
            sp.GetRequiredService<SettingsWindowViewModel>(),
            sp.GetRequiredService<IHelpContextProvider>()));
        // Mit dem Scope endet auch das ViewModel, und ein noch laufender Download bricht ab.
        _ = services.AddSingleton<Func<SettingsWindow>>(sp =>
            () => WindowScope.Create(sp, scoped => scoped.GetRequiredService<SettingsWindow>()));

        _ = services.AddTransient(sp => new GraphViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<UiSettingsStore>(),
            sp.GetRequiredService<ILogger<GraphViewModel>>(),
            sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        _ = services.AddTransient(sp => new GraphWindow(
            sp.GetRequiredService<GraphViewModel>(),
            sp.GetRequiredService<IHelpContextProvider>(),
            sp.GetRequiredService<IEffectiveThemeProvider>(),
            // Jedes Fenster bekommt eine eigene Datei; mit einer gemeinsamen lüde ein Fenster
            // beim Neuladen den Graphen eines anderen.
            new GraphSnapshotFile(
                sp.GetRequiredService<IFileSystem>(),
                Path.Combine(AppPaths.Default.ApplicationDataDirectory, "webview2"),
                Guid.NewGuid()),
            sp.GetRequiredService<IExternalLinkLauncher>(),
            sp.GetRequiredService<ILogger<GraphWindow>>()));
        _ = services.AddSingleton<Func<GraphWindow>>(sp => () => sp.GetRequiredService<GraphWindow>());

        _ = services.AddSingleton<ITagManagementDialogService>(sp => new TagManagementDialogService());
        // Ein Scope je Fenster: Die Dienste dahinter sind scoped und halten einen DbContext. Er
        // lebt so lange wie das Fenster und wird beim Schließen mit freigegeben.
        _ = services.AddTransient(sp => new TagManagementWindow(new TagManagementViewModel(
            sp.GetRequiredService<ITagStatisticsService>(),
            sp.GetRequiredService<ITagManagementService>(),
            sp.GetRequiredService<ITagManagementDialogService>(),
            sp.GetRequiredService<ILogger<TagManagementViewModel>>())));
        _ = services.AddSingleton<Func<TagManagementWindow>>(sp =>
            () => WindowScope.Create(sp, scoped => scoped.GetRequiredService<TagManagementWindow>()));

        _ = services.AddSingleton<IHelpContextProvider>(sp => new HelpContextProvider());
        _ = services.AddSingleton<IHelpContentService>(sp => new HelpContentService(
            sp.GetRequiredService<ILogger<HelpContentService>>()));
        _ = services.AddSingleton<IAboutInfoProvider>(sp => new AboutInfoProvider(sp.GetRequiredService<TimeProvider>()));
        _ = services.AddSingleton(sp => new HelpWindowGeometryStore(sp.GetRequiredService<UiSettingsStore>()));
        _ = services.AddTransient(sp => new HelpViewModel());
        _ = services.AddTransient(sp => new HelpWindow(
            sp.GetRequiredService<HelpViewModel>(),
            sp.GetRequiredService<IHelpContentService>(),
            sp.GetRequiredService<IEffectiveThemeProvider>(),
            sp.GetRequiredService<HelpWindowGeometryStore>(),
            sp.GetRequiredService<IExternalLinkLauncher>(),
            sp.GetRequiredService<ILogger<HelpWindow>>()));
        _ = services.AddSingleton<Func<HelpWindow>>(sp => () => sp.GetRequiredService<HelpWindow>());
        _ = services.AddTransient(sp => new AboutViewModel(
            sp.GetRequiredService<IAboutInfoProvider>(),
            sp.GetRequiredService<TimeProvider>()));
        _ = services.AddTransient(sp => new AboutWindow(
            sp.GetRequiredService<AboutViewModel>(),
            sp.GetRequiredService<IExternalLinkLauncher>()));
        _ = services.AddSingleton<Func<AboutWindow>>(sp => () => sp.GetRequiredService<AboutWindow>());

        _ = services.AddTransient(sp => new LogViewerViewModel(
            sp.GetRequiredService<IMemoryLogStore>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IFileSaveDialogService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<LogViewerViewModel>>()));
        _ = services.AddTransient(sp => new LogViewerWindow(sp.GetRequiredService<LogViewerViewModel>()));
        // Das ViewModel hält eine Kopie des Protokolls; ohne eigenen Scope bliebe sie nach jedem
        // Öffnen bis zum Ende der Anwendung im Container liegen.
        _ = services.AddSingleton<Func<LogViewerWindow>>(sp =>
            () => WindowScope.Create(sp, scoped => scoped.GetRequiredService<LogViewerWindow>()));

        _ = services.AddSingleton(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            new MainWindowPanels(
                sp.GetRequiredService<DocumentPanel>(),
                sp.GetRequiredService<AllFilesPanel>(),
                sp.GetRequiredService<TagCloudPanel>()),
            new SecondaryWindowFactories(
                sp.GetRequiredService<Func<SettingsWindow>>(),
                sp.GetRequiredService<Func<GraphWindow>>(),
                sp.GetRequiredService<Func<TagManagementWindow>>(),
                sp.GetRequiredService<Func<HelpWindow>>(),
                sp.GetRequiredService<Func<AboutWindow>>(),
                sp.GetRequiredService<Func<LogViewerWindow>>()),
            sp.GetRequiredService<IHelpContextProvider>(),
            sp.GetRequiredService<IExternalLinkLauncher>()));
        _ = services.AddSingleton(sp => new SplashWindow());

        return services;
    }
}
