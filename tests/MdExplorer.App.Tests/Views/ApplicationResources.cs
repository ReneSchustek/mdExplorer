using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MdExplorer.App.Controls;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Legt dieselben Wörterbücher an, die <c>App.xaml</c> zusammenführt.
/// </summary>
/// <remarks>
/// Es entsteht kein Fenster: Zum Auflösen von <c>StaticResource</c> genügt eine Anwendung mit
/// gefüllten Ressourcen. Die Anwendung wird nur angelegt, wenn es noch keine gibt, denn der
/// Testläufer führt mehrere Prüfungen im selben Prozess aus. Nur auf einem STA-Thread aufrufen
/// (<see cref="StaRunner"/>).
/// </remarks>
internal static class ApplicationResources
{
    /// <summary>Die Wörterbücher aus <c>App.xaml</c>, in derselben Reihenfolge.</summary>
    private static readonly string[] ThemeDictionaries =
    [
        "/Themes/Tokens.xaml",
        "/Themes/Light.xaml",
        "/Themes/ControlStyles.xaml",
    ];

    /// <summary>Name der Bibliothek, in der die Bausteine und die Belegung liegen.</summary>
    private static readonly string AppAssemblyName =
        typeof(SearchBox).Assembly.GetName().Name ?? "MdExplorer";

    // Die Anwendung gibt es nur einmal je Prozess, und Testklassen laufen parallel. Ohne Sperre
    // legten zwei Threads sie zugleich an, und die Wörterbücher, die beim ersten Zugriff ihren
    // Inhalt nachladen, würden von zweien gleichzeitig gefüllt.
    private static readonly Lock Gate = new();

    /// <summary>Führt <paramref name="body"/> mit den Ressourcen der Anwendung aus.</summary>
    /// <param name="body">Der Prüfteil.</param>
    public static void Use(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        lock (Gate)
        {
            EnsureApplication();
            body();
        }
    }

    /// <summary>Liest einen Eintrag aus den Ressourcen der Anwendung.</summary>
    /// <param name="key">Schlüssel des Eintrags.</param>
    public static object Resource(object key) => Application.Current.FindResource(key);

    private static void EnsureApplication()
    {
        Application application = Application.Current ?? CreateApplication();
        if (application.Resources.MergedDictionaries.Count == 0)
        {
            foreach (string path in ThemeDictionaries)
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    // Der Name kommt aus dem Steuerelement selbst und nicht aus einer
                    // abgeschriebenen Zeichenkette: Der Projektname und der Name der erzeugten
                    // Bibliothek sind hier nicht dasselbe.
                    Source = new Uri(
                        "pack://application:,,,/" + AppAssemblyName + ";component" + path,
                        UriKind.Absolute),
                });
            }

            application.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        }
    }

    /// <summary>Legt die Anwendung auf einem eigenen Thread an, der bis zum Prozessende läuft.</summary>
    /// <remarks>
    /// Gehörte sie dem Thread der Prüfung, die zufällig zuerst kommt, beendete WPF sie, sobald dort
    /// ein Fenster schließt. Eine zweite Anwendung lässt sich im selben Prozess nicht anlegen; jede
    /// spätere Prüfung mit Ressourcen scheiterte dann, und welche Prüfung zuerst kommt, hängt von der
    /// Reihenfolge der Testklassen ab. Auf dem eigenen Thread entsteht kein Fenster, und
    /// <see cref="ShutdownMode.OnExplicitShutdown"/> hält sie auch sonst am Leben.
    /// </remarks>
    private static Application CreateApplication()
    {
        Application? created = null;
        using ManualResetEventSlim ready = new();

        Thread thread = new(() =>
        {
            created = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Testanwendung",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        return created!; // vor ready.Set() gesetzt, ready.Wait() wartet darauf
    }
}
