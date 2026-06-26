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

namespace MdExplorer.App.Hosting;

/// <summary>
/// Baut den Generic Host der Anwendung: Logging, Optionen, DI-Registrierung der Schichten.
/// Verwendet Factory-Lambdas für UI-Klassen, damit der Analyzer den Constructor-Aufruf sieht
/// und CA1812 nicht greift.
/// </summary>
internal static class AppHostBuilder
{
    /// <summary>
    /// Erzeugt einen vollständig konfigurierten <see cref="IHost"/>.
    /// </summary>
    public static IHost Build()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        _ = builder.Logging.ClearProviders();
        _ = builder.Services.AddSerilog();

        _ = builder.Services
            .AddOptions<DatabaseOptions>()
            .Configure(options => options.DatabasePath = AppPaths.GetDatabasePath())
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<IndexerOptions>()
            .Bind(builder.Configuration.GetSection(IndexerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<ParserOptions>()
            .Bind(builder.Configuration.GetSection(ParserOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<SearchOptions>()
            .Bind(builder.Configuration.GetSection(SearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<TagCloudOptions>()
            .Bind(builder.Configuration.GetSection(TagCloudOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<GraphOptions>()
            .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services
            .AddOptions<UpdateOptions>()
            .Bind(builder.Configuration.GetSection(UpdateOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = builder.Services.AddCore();
        _ = builder.Services.AddData();
        _ = builder.Services.AddIndexer();
        _ = builder.Services.AddParser();
        _ = builder.Services.AddSearch();
        _ = builder.Services.AddTagCloud();
        _ = builder.Services.AddGraph();
        _ = builder.Services.AddUpdate();

        _ = builder.Services.AddSingleton<ISystemThemeProvider>(sp => new SystemThemeProvider());
        _ = builder.Services.AddSingleton(sp => new PreviewHtmlBuilder(sp.GetRequiredService<ISystemThemeProvider>()));
        _ = builder.Services.AddSingleton(sp => new UiSettingsStore(sp.GetRequiredService<ILogger<UiSettingsStore>>()));
        _ = builder.Services.AddSingleton<IDocumentLocator>(sp =>
            new MarkdownFileDocumentLocator(sp.GetRequiredService<IServiceScopeFactory>()));
        _ = builder.Services.AddSingleton<IDialogService, DialogService>();
        _ = builder.Services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        _ = builder.Services.AddSingleton<IFileSaveDialogService, FileSaveDialogService>();
        _ = builder.Services.AddSingleton<IMemoryLogStore>(_ => MemorySink.Instance);
        _ = builder.Services.AddSingleton<IOperationHealthProvider>(sp => new OperationHealthProvider(
            sp.GetRequiredService<IMemoryLogStore>()));

        _ = builder.Services.AddSingleton(sp => new FolderTreeViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<IExclusionFilter>()));
        _ = builder.Services.AddSingleton(sp => new AllFilesViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<AllFilesViewModel>>()));
        _ = builder.Services.AddSingleton(sp => new SearchViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<ILogger<SearchViewModel>>()));
        _ = builder.Services.AddSingleton(sp => new TagCloudViewModel(
            sp.GetRequiredService<ITagStatisticsService>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<IOptions<TagCloudOptions>>(),
            sp.GetRequiredService<ILogger<TagCloudViewModel>>()));
        _ = builder.Services.AddSingleton(sp => new PreviewViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<PreviewHtmlBuilder>(),
            sp.GetRequiredService<ILogger<PreviewViewModel>>()));
        _ = builder.Services.AddSingleton<IEditorConfirmationDialogService, EditorConfirmationDialogService>();
        _ = builder.Services.AddSingleton(sp => new MarkdownEditorViewModel(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ITagExtractor>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IEditorConfirmationDialogService>(),
            sp.GetRequiredService<ILogger<MarkdownEditorViewModel>>()));
        _ = builder.Services.AddSingleton(sp => new DocumentPanelViewModel(
            sp.GetRequiredService<PreviewViewModel>(),
            sp.GetRequiredService<MarkdownEditorViewModel>(),
            sp.GetRequiredService<IMarkdownParser>(),
            sp.GetRequiredService<PreviewHtmlBuilder>(),
            sp.GetRequiredService<IDocumentLocator>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ILogger<DocumentPanelViewModel>>()));
        _ = builder.Services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<FolderTreeViewModel>(),
            sp.GetRequiredService<AllFilesViewModel>(),
            sp.GetRequiredService<SearchViewModel>(),
            sp.GetRequiredService<DocumentPanelViewModel>(),
            sp.GetRequiredService<TagCloudViewModel>(),
            sp.GetRequiredService<IDocumentLocator>(),
            sp.GetRequiredService<UiSettingsStore>(),
            sp.GetRequiredService<IOperationHealthProvider>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<MdExplorer.Indexer.Abstractions.IIndexer>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MainViewModel>>()));
        _ = builder.Services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());

        _ = builder.Services.AddSingleton(sp => new PreviewPanel(
            sp.GetRequiredService<INavigationService>(),
            sp.GetRequiredService<ILogger<PreviewPanel>>()));
        _ = builder.Services.AddSingleton(sp => new DocumentPanel(sp.GetRequiredService<PreviewPanel>()));
        _ = builder.Services.AddSingleton(sp => new AllFilesPanel());
        _ = builder.Services.AddSingleton(sp => new TagCloudPanel());

        _ = builder.Services.AddSingleton(sp => new SplashViewModel());
        _ = builder.Services.AddTransient(sp => new SettingsWindowViewModel(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<SettingsValidator>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<ILogger<SettingsWindowViewModel>>()));
        _ = builder.Services.AddTransient(sp => new SettingsWindow(
            sp.GetRequiredService<SettingsWindowViewModel>(),
            sp.GetRequiredService<IHelpContextProvider>()));
        _ = builder.Services.AddSingleton<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());

        _ = builder.Services.AddTransient(sp => new GraphViewModel(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<UiSettingsStore>(),
            sp.GetRequiredService<ILogger<GraphViewModel>>()));
        _ = builder.Services.AddTransient(sp => new GraphWindow(
            sp.GetRequiredService<GraphViewModel>(),
            sp.GetRequiredService<IHelpContextProvider>()));
        _ = builder.Services.AddSingleton<Func<GraphWindow>>(sp => () => sp.GetRequiredService<GraphWindow>());

        _ = builder.Services.AddSingleton<ITagManagementDialogService>(sp => new TagManagementDialogService());
        _ = builder.Services.AddTransient<TagManagementWindow>(sp =>
        {
            IServiceScope scope = sp.CreateScope();
            ITagStatisticsService statistics = scope.ServiceProvider.GetRequiredService<ITagStatisticsService>();
            ITagManagementService management = scope.ServiceProvider.GetRequiredService<ITagManagementService>();
            ITagManagementDialogService dialog = sp.GetRequiredService<ITagManagementDialogService>();
            ILogger<TagManagementViewModel> logger = sp.GetRequiredService<ILogger<TagManagementViewModel>>();
            TagManagementViewModel viewModel = new(statistics, management, dialog, logger);
            TagManagementWindow window = new(viewModel);
            window.Closed += (_, _) => scope.Dispose();
            return window;
        });
        _ = builder.Services.AddSingleton<Func<TagManagementWindow>>(sp => () => sp.GetRequiredService<TagManagementWindow>());

        _ = builder.Services.AddSingleton<IHelpContextProvider>(sp => new HelpContextProvider());
        _ = builder.Services.AddSingleton<IHelpContentService>(sp => new HelpContentService(
            sp.GetRequiredService<ILogger<HelpContentService>>()));
        _ = builder.Services.AddSingleton<IAboutInfoProvider>(sp => new AboutInfoProvider());
        _ = builder.Services.AddSingleton(sp => new HelpWindowGeometryStore(sp.GetRequiredService<UiSettingsStore>()));
        _ = builder.Services.AddTransient(sp => new HelpWindow(
            sp.GetRequiredService<IHelpContentService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<HelpWindowGeometryStore>(),
            sp.GetRequiredService<ILogger<HelpWindow>>()));
        _ = builder.Services.AddSingleton<Func<HelpWindow>>(sp => () => sp.GetRequiredService<HelpWindow>());
        _ = builder.Services.AddTransient(sp => new AboutViewModel(sp.GetRequiredService<IAboutInfoProvider>()));
        _ = builder.Services.AddTransient(sp => new AboutWindow(sp.GetRequiredService<AboutViewModel>()));
        _ = builder.Services.AddSingleton<Func<AboutWindow>>(sp => () => sp.GetRequiredService<AboutWindow>());

        _ = builder.Services.AddTransient(sp => new LogViewerViewModel(
            sp.GetRequiredService<IMemoryLogStore>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IFileSaveDialogService>()));
        _ = builder.Services.AddTransient(sp => new LogViewerWindow(sp.GetRequiredService<LogViewerViewModel>()));
        _ = builder.Services.AddSingleton<Func<LogViewerWindow>>(sp => () => sp.GetRequiredService<LogViewerWindow>());

        _ = builder.Services.AddSingleton(sp => new MainWindow(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<DocumentPanel>(),
            sp.GetRequiredService<AllFilesPanel>(),
            sp.GetRequiredService<TagCloudPanel>(),
            sp.GetRequiredService<Func<SettingsWindow>>(),
            sp.GetRequiredService<Func<GraphWindow>>(),
            sp.GetRequiredService<Func<TagManagementWindow>>(),
            sp.GetRequiredService<Func<HelpWindow>>(),
            sp.GetRequiredService<Func<AboutWindow>>(),
            sp.GetRequiredService<Func<LogViewerWindow>>(),
            sp.GetRequiredService<IHelpContextProvider>()));
        _ = builder.Services.AddSingleton(sp => new SplashWindow());

        _ = builder.Services.AddSingleton<SettingsChangeBridge>();
        _ = builder.Services.AddHostedService(sp => sp.GetRequiredService<SettingsChangeBridge>());

        _ = builder.Services.AddSingleton<IndexerProgressBridge>();
        _ = builder.Services.AddHostedService(sp => sp.GetRequiredService<IndexerProgressBridge>());

        _ = builder.Services.AddHostedService(sp => new UpdateCheckBackgroundService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<UpdateCheckBackgroundService>>()));

        return builder.Build();
    }
}
