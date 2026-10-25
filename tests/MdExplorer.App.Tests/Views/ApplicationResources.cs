using System.Windows;
using System.Windows.Controls;
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
        Application application = Application.Current ?? new Application();
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
}
