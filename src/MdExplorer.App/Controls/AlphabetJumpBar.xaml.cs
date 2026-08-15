using System.Collections;
using System.Windows;
using System.Windows.Input;

namespace MdExplorer.App.Controls;

/// <summary>
/// Sprungleiste der Gestaltungslinie.
/// </summary>
internal sealed partial class AlphabetJumpBar
{
    /// <summary>Die Buchstaben samt Angabe, ob sie Einträge haben.</summary>
    public static readonly DependencyProperty LettersProperty = DependencyProperty.Register(
        nameof(Letters),
        typeof(IEnumerable),
        typeof(AlphabetJumpBar),
        new PropertyMetadata(null));

    /// <summary>Der Befehl, der den Sprung auslöst.</summary>
    public static readonly DependencyProperty JumpCommandProperty = DependencyProperty.Register(
        nameof(JumpCommand),
        typeof(ICommand),
        typeof(AlphabetJumpBar),
        new PropertyMetadata(null));

    /// <summary>Erzeugt die Sprungleiste.</summary>
    public AlphabetJumpBar()
    {
        InitializeComponent();
    }

    /// <inheritdoc cref="LettersProperty" />
    public IEnumerable? Letters
    {
        get => (IEnumerable?)GetValue(LettersProperty);
        set => SetValue(LettersProperty, value);
    }

    /// <inheritdoc cref="JumpCommandProperty" />
    public ICommand? JumpCommand
    {
        get => (ICommand?)GetValue(JumpCommandProperty);
        set => SetValue(JumpCommandProperty, value);
    }
}
