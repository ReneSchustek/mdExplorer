using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MdExplorer.App.Controls;
using MdExplorer.App.Localization;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Baut die drei Bausteine der Gestaltungslinie wirklich auf.
/// </summary>
/// <remarks>
/// <para>
/// Jeder der drei greift über <c>StaticResource</c> auf die Belegung zu, etwa auf
/// <c>PagePadding</c>, <c>FontSizeSubtitle</c>, <c>ControlCornerRadius</c> und
/// <c>BoolToVisibility</c>. Wird einer dieser Namen umbenannt, bleibt der Bau grün, weil das
/// Markup erst beim Erzeugen gelesen wird. Der Fehler zeigt sich dann im laufenden Programm, an der Stelle, an der die
/// Ansicht leer bleibt.
/// </para>
/// <para>
/// Diese Prüfungen legen die Wörterbücher genauso an wie die Anwendung und erzeugen jeden
/// Baustein einmal. Ein fehlender Name lässt sie hier scheitern statt beim Nutzer.
/// </para>
/// </remarks>
public sealed class DesignLineControlTests
{
    /// <summary>Buchstaben für die Sprungleiste.</summary>
    private static readonly string[] Letters = ["A", "B", "C"];

    [Fact]
    public void AlphabetJumpBar_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            AlphabetJumpBar sut = new();

            Assert.Null(sut.Letters);
            Assert.Null(sut.JumpCommand);
        });
    });

    [Fact]
    public void AlphabetJumpBar_CarriesLettersAndCommand() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            IEnumerable letters = Letters;
            PlaceholderCommand command = new();

            AlphabetJumpBar sut = new() { Letters = letters, JumpCommand = command };

            Assert.Same(letters, sut.Letters);
            Assert.Same(command, sut.JumpCommand);
        });
    });

    [Fact]
    public void SearchBox_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            SearchBox sut = new();

            Assert.Empty(sut.Text);
            Assert.Equal(UiText.Get("SearchBox_Placeholder"), sut.Placeholder, StringComparer.Ordinal);
        });
    });

    /// <remarks>
    /// Gesetzter Text und eigener Platzhalter kommen unverändert an.
    /// </remarks>
    [Fact]
    public void SearchBox_KeepsTheTypedText() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
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
        ApplicationResources.Use(() =>
        {
            SearchBox sut = new() { Text = "vorher" };

            sut.FocusInput();

            // Ohne Fenster nimmt nichts den Fokus an; geprüft ist, dass der Zugriff auf das
            // Feld gelingt und der Inhalt dabei unangetastet bleibt.
            Assert.Equal("vorher", sut.Text, StringComparer.Ordinal);
        });
    });

    [Fact]
    public void EmptyState_BuildsAgainstTheRealPalette() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            EmptyState sut = new();

            Assert.False(sut.ShowsReset);
        });
    });

    [Fact]
    public void EmptyState_CarriesItsTexts() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
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

    /// <remarks>
    /// Die Zusage des Bausteins: Die Escape-Taste leert das Feld. Wer sucht und danebengreift,
    /// kommt mit einem Griff zurück, ohne die Hand von der Tastatur zu nehmen. Der Weg dorthin
    /// führt über das benannte Eingabefeld aus dem Markup; beide Enden werden hier geprüft.
    /// </remarks>
    [Fact]
    public void SearchBox_OnEscape_ClearsTheText() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
        {
            SearchBox sut = new() { Text = "bericht" };
            TextBox input = FindInput(sut);

            KeyEventArgs escape = new(
                Keyboard.PrimaryDevice,
                new HwndSourceStub(),
                timestamp: 0,
                Key.Escape)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            input.RaiseEvent(escape);

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
        ApplicationResources.Use(() =>
        {
            SearchBox sut = new() { Text = "bericht" };
            TextBox input = FindInput(sut);

            KeyEventArgs enter = new(
                Keyboard.PrimaryDevice,
                new HwndSourceStub(),
                timestamp: 0,
                Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            input.RaiseEvent(enter);

            Assert.Equal("bericht", sut.Text, StringComparer.Ordinal);
            Assert.False(enter.Handled);
        });
    });

    /// <remarks>
    /// Derselbe Vorgang über die Schaltfläche im Feld, für alle, die mit der Maus arbeiten.
    /// </remarks>
    [Fact]
    public void SearchBox_OnClearButton_ClearsTheText() => StaRunner.Run(() =>
    {
        ApplicationResources.Use(() =>
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
        Button? found = Descendants(box).OfType<Button>().FirstOrDefault();
        Assert.NotNull(found);
        return found;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (DependencyObject kind in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return kind;
            foreach (DependencyObject grandchild in Descendants(kind))
            {
                yield return grandchild;
            }
        }
    }

    /// <summary>Platzhalter-Befehl; geprüft wird nur, dass er unverändert am Baustein ankommt.</summary>
    private sealed class PlaceholderCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ein Ereignis-Ursprung für Tastatur-Ereignisse ohne Fenster.</summary>
    private sealed class HwndSourceStub : PresentationSource
    {
        public override bool IsDisposed => false;

        public override Visual? RootVisual { get; set; }

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
