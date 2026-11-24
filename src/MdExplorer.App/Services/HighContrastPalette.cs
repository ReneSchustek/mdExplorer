using System.Windows;
using System.Windows.Media;

namespace MdExplorer.App.Services;

/// <summary>
/// Die Belegung im Windows-Kontrastmodus: dieselben Schlüssel wie <c>Themes/Light.xaml</c>, die
/// Farben aber aus <see cref="SystemColors"/>, also aus dem Kontrastdesign, das der Nutzer in
/// Windows gewählt hat.
/// </summary>
/// <remarks>
/// <para>
/// Windows sichert Kontrast nur für seine eigenen Paare zu: Fenstertext auf Fenster,
/// Hervorhebungstext auf Hervorhebung, ausgegrauter Text auf Fenster. Jede Fläche, auf der Text der
/// Oberfläche stehen kann, wird deshalb Fenster, und jeder solche Text Fenstertext. Die Hervorhebung
/// bekommen nur Flächen, deren Text eigens dazugehört (Akzentknopf, Suchtreffer).
/// </para>
/// <para>
/// Eine Auswahl färbt deshalb keine Fläche: Eine ausgewählte Listenzeile trägt auch ihren grauen
/// Pfad, und der stünde auf der Hervorhebung ohne zugesicherten Kontrast. Sie zeigt sich über
/// ihren Rand (<c>SelectionBorderBrush</c>, <c>SelectionOutlineBrush</c>). Mitgelieferte Muster wie
/// das des Baums färben ihre Auswahl selbst über die Systemfarbe der Hervorhebung; diese und ihr
/// Text werden deshalb hier auf das Fensterpaar gestellt, wie es Hell und Dunkel mit ihren Farben tun.
/// </para>
/// <para>
/// Im Code statt als XAML-Datei, weil die Farben erst beim Anwenden aus Windows gelesen werden;
/// wechselt der Nutzer das Kontrastdesign, baut <see cref="ThemeApplier"/> die Belegung neu.
/// </para>
/// </remarks>
internal static class HighContrastPalette
{
    private static readonly string[] Window =
    [
        "AppBackgroundBrush", "SurfaceBrush", "SurfaceElevatedBrush", "SurfaceHoverBrush",
        "ButtonBackgroundDisabledBrush", "ButtonSecondaryBackgroundBrush", "ButtonSecondaryBackgroundHoverBrush",
        "CardBackgroundBrush", "CardBackgroundHoverBrush", "ToolBarBackgroundBrush", "StatusBarBackgroundBrush",
        "InputBackgroundBrush", "InputBackgroundHoverBrush", "InputBackgroundFocusBrush", "InputBackgroundDisabledBrush",
        "SelectionBackgroundBrush", "NoticeWarningBackgroundBrush", "NoticeInfoBackgroundBrush", "TagBackgroundBrush",
    ];

    // Text und Linien auf Fensterflächen. Die Zustandsfarben gehören dazu: Neben dem Punkt der
    // Betriebsanzeige steht der Zustand als Wort, die Farbe trägt ihn nicht allein.
    private static readonly string[] WindowText =
    [
        "TextPrimaryBrush", "TextSecondaryBrush", "TextTertiaryBrush", "ButtonSecondaryForegroundBrush",
        "StatusBarForegroundBrush", "StatusBarErrorBrush", "NoticeWarningForegroundBrush", "NoticeInfoForegroundBrush",
        "TagForegroundBrush", "CardBorderBrush", "ButtonSecondaryBorderBrush", "InputBorderBrush", "DividerBrush",
        "SplitterBrush", "NoticeWarningBorderBrush", "NoticeInfoBorderBrush", "TagBorderBrush",
        "StateSuccessBrush", "StateWarningBrush", "StateErrorBrush", "StateNeutralBrush",
    ];

    private static readonly string[] GrayText =
    [
        "TextDisabledBrush", "ButtonForegroundDisabledBrush", "AccentPrimaryDisabledBrush",
    ];

    // Hervorhebung als Fläche nur zusammen mit Hervorhebungstext; als Vordergrund (Verweise,
    // Buchstabenleiste) und als Rand (Fokus, Auswahl) steht sie auf Fenster.
    private static readonly string[] Highlight =
    [
        "AccentPrimaryBrush", "AccentPrimaryHoverBrush", "AccentPrimaryPressedBrush", "AccentSoftBrush",
        "CardBorderHoverBrush", "InputBorderHoverBrush", "InputBorderFocusBrush", "SelectionBorderBrush",
        "SelectionOutlineBrush", "SearchHighlightBackgroundBrush",
    ];

    private static readonly string[] HighlightText =
    [
        "TextOnAccentBrush", "SearchHighlightForegroundBrush",
    ];

    /// <summary>Baut die Belegung aus den Farben, die Windows gerade vorgibt.</summary>
    public static ResourceDictionary Create()
    {
        ResourceDictionary palette = [];
        AddAll(palette, Window, SystemColors.WindowColor);
        AddAll(palette, WindowText, SystemColors.WindowTextColor);
        AddAll(palette, GrayText, SystemColors.GrayTextColor);
        AddAll(palette, Highlight, SystemColors.HighlightColor);
        AddAll(palette, HighlightText, SystemColors.HighlightTextColor);
        AddSystem(palette, SystemColors.HighlightBrushKey, SystemColors.WindowColor);
        AddSystem(palette, SystemColors.HighlightTextBrushKey, SystemColors.WindowTextColor);
        AddSystem(palette, SystemColors.InactiveSelectionHighlightBrushKey, SystemColors.WindowColor);
        AddSystem(palette, SystemColors.InactiveSelectionHighlightTextBrushKey, SystemColors.WindowTextColor);
        return palette;
    }

    private static void AddAll(ResourceDictionary palette, string[] keys, Color color)
    {
        foreach (string key in keys)
        {
            palette.Add(key, Frozen(color));
        }
    }

    private static void AddSystem(ResourceDictionary palette, ResourceKey key, Color color) => palette.Add(key, Frozen(color));

    // Eingefroren, damit die Belegung wie die geladenen Dateien zwischen Threads lesbar ist.
    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}
