using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using MdExplorer.App.Services.Help;
using MdExplorer.App.ViewModels.Graph;
using MdExplorer.Core;

namespace MdExplorer.App.Views.Graph;

/// <summary>
/// WebView2-basiertes Graph-Fenster. Lädt die eingebetteten Assets
/// (HTML/CSS/JS), injiziert das aktuelle Snapshot-JSON über String-Replacement
/// und schreibt das Ergebnis in eine Temp-Datei unter <c>%LOCALAPPDATA%\MdExplorer\webview2</c>.
/// Die View navigiert per <c>file://</c>-URI dorthin — <see cref="Microsoft.Web.WebView2.Wpf.WebView2.NavigateToString(string)"/>
/// hat ein hartes Groessen-Limit (Edge meldet <c>E_INVALIDARG</c> oberhalb von ~2 MB),
/// das bei grossen Wikis (&gt;10k Knoten) regelmaessig ueberschritten wird.
/// </summary>
internal sealed partial class GraphWindow : Window
{
    private const string CssPlaceholder = "__INLINE_CSS__";
    private const string JsPlaceholder = "__INLINE_JS__";
    private const string JsonPlaceholder = "__GRAPH_JSON__";
    private const string NoncePlaceholder = "__SCRIPT_NONCE__";
    private const int NonceByteLength = 16;
    private const string SnapshotFileName = "graph-snapshot.html";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly GraphViewModel _viewModel;
    private readonly IHelpContextProvider _helpContextProvider;
    private readonly string _snapshotFilePath;
    private bool _isInitialized;

    /// <summary>Erzeugt das Fenster und verdrahtet das ViewModel.</summary>
    public GraphWindow(GraphViewModel viewModel, IHelpContextProvider helpContextProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(helpContextProvider);
        InitializeComponent();
        _viewModel = viewModel;
        _helpContextProvider = helpContextProvider;
        _snapshotFilePath = Path.Combine(AppPaths.GetApplicationDataDirectory(), "webview2", SnapshotFileName);
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoadedAsync;
        Activated += OnActivatedHandler;
        Closed += OnWindowClosed;
    }

    private void OnActivatedHandler(object? sender, EventArgs args) =>
        _helpContextProvider.SetSlug(HelpContext.Graph);

    private async void OnLoadedAsync(object sender, RoutedEventArgs args)
    {
        await GraphView.EnsureCoreWebView2Async().ConfigureAwait(true);
        _isInitialized = true;
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!_isInitialized || !string.Equals(args.PropertyName, nameof(GraphViewModel.SnapshotJson), StringComparison.Ordinal))
        {
            return;
        }
        string? json = _viewModel.SnapshotJson;
        if (string.IsNullOrEmpty(json))
        {
            return;
        }
        string html = BuildHtml(json);
        string? directory = Path.GetDirectoryName(_snapshotFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }
        File.WriteAllText(_snapshotFilePath, html, Utf8NoBom);

        // CoreWebView2.Navigate erzwingt einen Reload auch bei gleicher URI; ein Setter
        // auf Source ignoriert identische Werte und wuerde den frisch geschriebenen Inhalt
        // nicht laden. Der Cache-Buster verhindert zusaetzlich, dass Chromium die alte
        // Datei aus dem Memory-Cache liefert, wenn sich nur der Inhalt geaendert hat.
        string uri = new Uri(_snapshotFilePath).AbsoluteUri + "?t=" + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        GraphView.CoreWebView2.Navigate(uri);
    }

    internal static string BuildHtml(string snapshotJson)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        string htmlTemplate = ReadEmbeddedResource("graph.html");
        string css = ReadEmbeddedResource("graph.css");
        string js = ReadEmbeddedResource("graph.js");
        string nonce = GenerateNonce();
        // Reihenfolge bewusst: vertrauenswuerdige Platzhalter zuerst, das nutzerkontrollierte
        // Snapshot-JSON ganz zum Schluss — so ersetzt das Platzhalter-Replace im JSON nichts mehr.
        return htmlTemplate
            .Replace(CssPlaceholder, css, StringComparison.Ordinal)
            .Replace(JsPlaceholder, js, StringComparison.Ordinal)
            .Replace(NoncePlaceholder, nonce, StringComparison.Ordinal)
            .Replace(JsonPlaceholder, snapshotJson, StringComparison.Ordinal);
    }

    private static string GenerateNonce()
    {
        Span<byte> buffer = stackalloc byte[NonceByteLength];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    private static string ReadEmbeddedResource(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Assembly assembly = typeof(GraphWindow).Assembly;
        string resourceName = "MdExplorer.App.Assets.Graph." + fileName;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Eingebettete Ressource fehlt: {resourceName}.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoadedAsync;
        Activated -= OnActivatedHandler;
        Closed -= OnWindowClosed;
        GraphView.Dispose();
    }
}
