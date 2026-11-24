using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using MdExplorer.App.ViewModels;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Win32;

namespace MdExplorer.App.Services;

/// <summary>
/// Setzt die gewählte Farbbelegung und hält sie bei späteren Änderungen nach: Wörterbücher der
/// Anwendung, Vorschau und die Titelleisten aller Fenster. Nachgezogen wird bei geänderten
/// Einstellungen und bei Änderungen in Windows (Kontrastmodus, Hell oder Dunkel bei der Wahl
/// „System“). Die Vorschau folgt außerdem der Schriftgröße und der Freigabe für Netzbilder.
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
    IEffectiveThemeProvider effectiveTheme) : IDisposable
{
    private AppliedTheme _applied = new(IsDarkMode: false, IsHighContrast: false);

    /// <summary>Setzt die Belegung und folgt ab jetzt jeder Änderung.</summary>
    public void Start()
    {
        ApplyPalette();
        FollowNewWindows();
        settings.SettingsChanged += OnSettingsChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Meldet sich von Einstellungen und Windows ab.</summary>
    /// <remarks>
    /// <see cref="SystemEvents"/> hält seine Abonnenten bis zum Abmelden fest; ohne Abmelden
    /// bliebe der Nachführer samt Vorschau erreichbar, solange der Prozess läuft.
    /// </remarks>
    public void Dispose()
    {
        settings.SettingsChanged -= OnSettingsChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
        {
            FollowWindows();
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (AppliedTheme.MayChangeTheTheme(args.Category))
        {
            FollowWindows();
        }
    }

    // Asynchron auf den Oberflächen-Thread: Die Meldung kommt auf dem Thread der Systemereignisse.
    private void FollowWindows() =>
        _ = application.Dispatcher.BeginInvoke(() =>
        {
            if (_applied.DiffersFrom(effectiveTheme))
            {
                ApplyChanges(themeChanged: true, previewChanged: true);
            }
        });

    private void ApplyPalette()
    {
        applier.Apply(application.Resources.MergedDictionaries);
        _applied = AppliedTheme.From(effectiveTheme);
    }

    private void ApplyChanges(bool themeChanged, bool previewChanged)
    {
        if (themeChanged)
        {
            ApplyPalette();
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
