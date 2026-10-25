using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;

namespace MdExplorer.App.Services;

/// <summary>
/// Setzt die gewählte Farbbelegung und hält sie bei späteren Änderungen nach: Wörterbücher der
/// Anwendung, Vorschau und die Titelleisten aller Fenster. Die Vorschau folgt außerdem der
/// Schriftgröße und der Freigabe für Netzbilder.
/// </summary>
/// <remarks>
/// Erst nach dem Start des Wirts, weil vorher weder Einstellungen noch Dienste bereitstehen. Das
/// Startbild trägt deshalb Markenfarben statt Belegungsfarben; es ist zu diesem Zeitpunkt bereits
/// sichtbar.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class ThemeFollower(
    Application application,
    ISettingsService settings,
    ThemeApplier applier,
    PreviewViewModel preview,
    IEffectiveThemeProvider effectiveTheme)
{
    /// <summary>Setzt die Belegung und folgt ab jetzt jeder Änderung.</summary>
    public void Start()
    {
        applier.Apply(application.Resources.MergedDictionaries);
        FollowNewWindows();
        settings.SettingsChanged += OnSettingsChanged;
    }

    // Jedes Fenster, sobald es steht: Die Titelleiste zeichnet Windows, und es fragt dafür nicht
    // die Anwendung. Ein Klassen-Ereignis erspart, jedes Fenster einzeln anzufassen, auch die,
    // die es künftig gibt.
    private void FollowNewWindows()
        => EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    WindowTitleBarTheme.Apply(window, effectiveTheme.IsDarkMode);
                }
            }));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        bool themeChanged = args.Previous.Appearance.Theme != args.Current.Appearance.Theme;
        bool previewChanged = PreviewHtmlBuilder.DependsOn(args.Previous, args.Current);
        if (!themeChanged && !previewChanged)
        {
            return;
        }

        // Der Wechsel muss auf den Oberflächen-Thread: Das Ereignis kommt von dort, wo
        // gespeichert wurde, und das ist nicht zwingend derselbe Thread. Der Austausch ist ein
        // Wörterbuch-Tausch und damit kurz genug, um darauf zu warten.
        application.Dispatcher.Invoke(() => ApplyChanges(themeChanged, previewChanged));
    }

    private void ApplyChanges(bool themeChanged, bool previewChanged)
    {
        if (themeChanged)
        {
            applier.Apply(application.Resources.MergedDictionaries);
            // Ebenso die Titelleisten der bereits offenen Fenster.
            foreach (Window openWindow in application.Windows)
            {
                WindowTitleBarTheme.Apply(openWindow, effectiveTheme.IsDarkMode);
            }
        }

        // Die Vorschau ist ein fertiges HTML-Dokument und hört auf keinen Wörterbuch-Tausch. Sie
        // wird ausdrücklich neu aufgebaut, auch nach einer neuen Schriftgröße.
        if (previewChanged)
        {
            preview.RebuildForSettings();
        }
    }
}
