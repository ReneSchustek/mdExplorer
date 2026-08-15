using System.Windows;
using System.Windows.Input;

namespace MdExplorer.App.Controls;

/// <summary>
/// Suchfeld der Gestaltungslinie.
/// </summary>
/// <remarks>
/// Als Steuerelement und nicht als abgeschriebenes Markup: Wer den Baustein fertig
/// vorfindet, baut ihn nicht nach — und die Wiedererkennung überlebt die dritte Seite.
/// </remarks>
internal sealed partial class SearchBox
{
    /// <summary>Der eingegebene Suchtext.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Sagt, was durchsucht wird — nicht bloß „Suchen".</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchBox),
        new PropertyMetadata("Suchen …"));

    /// <summary>Erzeugt das Suchfeld.</summary>
    public SearchBox()
    {
        InitializeComponent();
    }

    /// <inheritdoc cref="TextProperty" />
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="PlaceholderProperty" />
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Setzt den Eingabefokus ins Feld und wählt den Inhalt aus.</summary>
    /// <remarks>
    /// Wer über das Tastenkürzel herkommt, will tippen und nicht erst löschen — deshalb ist
    /// der bisherige Inhalt gleich markiert.
    /// </remarks>
    public void FocusInput()
    {
        _ = Input.Focus();
        Input.SelectAll();
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => Clear();

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e is null || e.Key != Key.Escape)
        {
            return;
        }

        Clear();
        e.Handled = true;
    }

    private void Clear()
    {
        Text = string.Empty;
        _ = Input.Focus();
    }
}
