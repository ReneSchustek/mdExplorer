using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MdExplorer.App.Controls;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Baut die drei Bausteine der Gestaltungslinie wirklich auf.
/// </summary>
/// <remarks>
/// <para>
/// Der Grund ist nicht die Abdeckungszahl, sondern das, was hier schiefgehen kann: Jeder der
/// drei greift über <c>StaticResource</c> auf die Belegung zu — <c>PagePadding</c>,
/// <c>FontSizeSubtitle</c>, <c>ControlCornerRadius</c>, <c>BoolToVisibility</c>. Wird einer
/// dieser Namen umbenannt, **bleibt der Bau grün**: Das Markup wird erst beim Erzeugen
/// gelesen. Der Fehler zeigt sich dann im laufenden Programm, an der Stelle, an der die
/// Ansicht leer bleibt.
/// </para>
/// <para>
/// Diese Prüfungen legen die Wörterbücher genauso an wie die Anwendung und erzeugen jeden
/// Baustein einmal. Ein fehlender Name lässt sie hier scheitern statt beim Nutzer.
/// </para>
/// </remarks>
public sealed class DesignLineControlTests
{
    /// <summary>Die Wörterbücher, die <c>App.xaml</c> zusammenführt — in derselben Reihenfolge.</summary>
    private static readonly string[] ThemeDictionaries =
    [
        "/Themes/Tokens.xaml",
        "/Themes/Light.xaml",
        "/Themes/ControlStyles.xaml",
    ];

    /// <summary>Name der Bibliothek, in der die Bausteine und die Belegung liegen.</summary>
    private static readonly string AppAssemblyName =
        typeof(SearchBox).Assembly.GetName().Name ?? "MdExplorer";

    /// <summary>Buchstaben für die Sprungleiste.</summary>
    private static readonly string[] Letters = ["A", "B", "C"];

    [Fact]
    public void AlphabetJumpBar_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            AlphabetJumpBar sut = new();

            Assert.Null(sut.Letters);
            Assert.Null(sut.JumpCommand);
        });
    });

    [Fact]
    public void AlphabetJumpBar_CarriesLettersAndCommand() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            IEnumerable buchstaben = Letters;
            RecordingCommand befehl = new();

            AlphabetJumpBar sut = new() { Letters = buchstaben, JumpCommand = befehl };

            Assert.Same(buchstaben, sut.Letters);
            Assert.Same(befehl, sut.JumpCommand);
        });
    });

    [Fact]
    public void SearchBox_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new();

            Assert.Empty(sut.Text);
            Assert.Equal("Suchen …", sut.Placeholder, StringComparer.Ordinal);
        });
    });

    /// <remarks>
    /// Die Zusage steht im Steuerelement: Wer über das Tastenkürzel herkommt, will tippen und
    /// nicht erst löschen. Geprüft wird, dass der Text zweiweg-fähig gesetzt bleibt.
    /// </remarks>
    [Fact]
    public void SearchBox_KeepsTheTypedText() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new() { Text = "bericht", Placeholder = "Notizen durchsuchen …" };

            Assert.Equal("bericht", sut.Text, StringComparer.Ordinal);
            Assert.Equal("Notizen durchsuchen …", sut.Placeholder, StringComparer.Ordinal);
        });
    });

    /// <remarks>
    /// <c>FocusInput</c> greift auf das benannte Eingabefeld aus dem Markup zu. Bekäme das Feld
    /// einen anderen Namen, fiele erst hier auf, dass das Tastenkürzel ins Leere greift.
    /// </remarks>
    [Fact]
    public void SearchBox_FocusInput_ReachesTheNamedInputField() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new() { Text = "vorher" };

            sut.FocusInput();

            // Ohne Fenster nimmt nichts den Fokus an — geprüft ist, dass der Zugriff auf das
            // Feld gelingt und der Inhalt dabei unangetastet bleibt.
            Assert.Equal("vorher", sut.Text, StringComparer.Ordinal);
        });
    });

    [Fact]
    public void EmptyState_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            EmptyState sut = new();

            Assert.False(sut.ShowsReset);
        });
    });

    [Fact]
    public void EmptyState_CarriesItsTexts() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            EmptyState sut = new()
            {
                Headline = "Nichts gefunden",
                Hint = "Andere Schreibweise versuchen.",
                ShowsReset = true,
            };

            Assert.Equal("Nichts gefunden", sut.Headline, StringComparer.Ordinal);
            Assert.Equal("Andere Schreibweise versuchen.", sut.Hint, StringComparer.Ordinal);
            Assert.True(sut.ShowsReset);
        });
    });

    /// <summary>
    /// Legt dieselben Wörterbücher an, die <c>App.xaml</c> zusammenführt.
    /// </summary>
    /// <remarks>
    /// Es entsteht bewusst **kein** Fenster: Zum Auflösen von <c>StaticResource</c> genügt eine
    /// Anwendung mit gefüllten Ressourcen. Die Anwendung wird nur angelegt, wenn es noch keine
    /// gibt — der Testläufer führt mehrere Prüfungen im selben Prozess aus.
    /// </remarks>
    private static void WithApplicationResources(Action body)
    {
        Application application = Application.Current ?? new Application();
        if (application.Resources.MergedDictionaries.Count == 0)
        {
            foreach (string pfad in ThemeDictionaries)
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    // Der Name kommt aus dem Steuerelement selbst und nicht aus einer
                    // abgeschriebenen Zeichenkette: Der Projektname und der Name der
                    // erzeugten Bibliothek sind hier nicht dasselbe.
                    Source = new Uri(
                        "pack://application:,,,/" + AppAssemblyName + ";component" + pfad,
                        UriKind.Absolute),
                });
            }

            application.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        }

        body();
    }

    /// <summary>Ein Befehl, der nur festhält, ob er ausgelöst wurde.</summary>
    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <remarks>
    /// Die Zusage des Bausteins: Die Escape-Taste leert das Feld. Wer sucht und danebengreift,
    /// kommt mit einem Griff zurück, ohne die Hand von der Tastatur zu nehmen. Der Weg dorthin
    /// führt über das benannte Eingabefeld aus dem Markup — beide Enden werden hier geprüft.
    /// </remarks>
    [Fact]
    public void SearchBox_OnEscape_ClearsTheText() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new() { Text = "bericht" };
            TextBox eingabe = FindInput(sut);

            KeyEventArgs escape = new(
                Keyboard.PrimaryDevice,
                new HwndSourceStub(),
                timestamp: 0,
                Key.Escape)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            eingabe.RaiseEvent(escape);

            Assert.Empty(sut.Text);
            Assert.True(escape.Handled);
        });
    });

    /// <remarks>
    /// Die Gegenprobe: Jede andere Taste lässt den Text stehen. Ein Feld, das bei einer
    /// beliebigen Taste leert, wäre unbenutzbar.
    /// </remarks>
    [Fact]
    public void SearchBox_OnAnyOtherKey_KeepsTheText() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new() { Text = "bericht" };
            TextBox eingabe = FindInput(sut);

            KeyEventArgs enter = new(
                Keyboard.PrimaryDevice,
                new HwndSourceStub(),
                timestamp: 0,
                Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            eingabe.RaiseEvent(enter);

            Assert.Equal("bericht", sut.Text, StringComparer.Ordinal);
            Assert.False(enter.Handled);
        });
    });

    /// <remarks>
    /// Derselbe Vorgang über die Schaltfläche im Feld — für alle, die mit der Maus arbeiten.
    /// </remarks>
    [Fact]
    public void SearchBox_OnClearButton_ClearsTheText() => StaRunner.Run(() =>
    {
        WithApplicationResources(() =>
        {
            SearchBox sut = new() { Text = "bericht" };
            Button clearButton = FindClearButton(sut);

            clearButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, clearButton));

            Assert.Empty(sut.Text);
        });
    });

    private static TextBox FindInput(SearchBox box) =>
        (TextBox)box.FindName("Input")!;

    private static Button FindClearButton(SearchBox box)
    {
        // Die Schaltfläche trägt keinen Namen; sie ist die einzige im Baustein.
        Button? gefunden = Descendants(box).OfType<Button>().FirstOrDefault();
        Assert.NotNull(gefunden);
        return gefunden;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject wurzel)
    {
        foreach (DependencyObject kind in LogicalTreeHelper.GetChildren(wurzel).OfType<DependencyObject>())
        {
            yield return kind;
            foreach (DependencyObject enkel in Descendants(kind))
            {
                yield return enkel;
            }
        }
    }

    /// <summary>Ein Ereignis-Ursprung für Tastatur-Ereignisse ohne Fenster.</summary>
    private sealed class HwndSourceStub : PresentationSource
    {
        public override bool IsDisposed => false;

        public override Visual? RootVisual { get; set; }

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
