using MdExplorer.App.Services;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Parser.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Baut den Editor so, wie die Anwendung ihn baut, mit einer Testuhr statt der Wanduhr.
/// </summary>
/// <remarks>
/// Die Wartezeit der Tag-Erkennung läuft nur, wenn ein Test die Uhr vorstellt. Ein Test, der auf
/// die echte Zeit wartete, fiele unter Last um und prüfte nebenbei den Planer des Betriebssystems.
/// </remarks>
internal static class TestEditors
{
    /// <summary>Fester Startpunkt der Testuhr, damit jeder Lauf dieselben Zeitstempel sieht.</summary>
    public static readonly DateTimeOffset Start = new(2026, 6, 12, 8, 0, 0, TimeSpan.Zero);

    public static MarkdownEditorViewModel Create(
        IFileSystem fileSystem,
        TimeProvider? timeProvider = null,
        IEditorConfirmationDialogService? confirmation = null) =>
        new(
            fileSystem,
            new TagExtractor(new FakeSettingsService(AppSettings.Default)),
            timeProvider ?? new FakeTimeProvider(Start),
            confirmation ?? new FakeEditorConfirmation(),
            NullLogger<MarkdownEditorViewModel>.Instance);
}
