using System.Windows;
using System.Windows.Input;

namespace MdExplorer.App.Controls;

/// <summary>
/// Leerzustand der Gestaltungslinie.
/// </summary>
internal sealed partial class EmptyState
{
    /// <summary>Die Aussage in einem Satz.</summary>
    public static readonly DependencyProperty HeadlineProperty = DependencyProperty.Register(
        nameof(Headline),
        typeof(string),
        typeof(EmptyState),
        new PropertyMetadata(string.Empty));

    /// <summary>Was der Nutzer tun kann.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint),
        typeof(string),
        typeof(EmptyState),
        new PropertyMetadata(string.Empty));

    /// <summary>Ob die Schaltfläche zum Zurücksetzen erscheint.</summary>
    public static readonly DependencyProperty ShowsResetProperty = DependencyProperty.Register(
        nameof(ShowsReset),
        typeof(bool),
        typeof(EmptyState),
        new PropertyMetadata(false));

    /// <summary>Der Befehl hinter der Schaltfläche.</summary>
    public static readonly DependencyProperty ResetCommandProperty = DependencyProperty.Register(
        nameof(ResetCommand),
        typeof(ICommand),
        typeof(EmptyState),
        new PropertyMetadata(null));

    /// <summary>Die Aufschrift der Schaltfläche — sie muss benennen, was zurückgeht.</summary>
    /// <remarks>
    /// Wo neben der Suche auch Filter wirken, wäre „Suche zurücksetzen" die halbe Wahrheit:
    /// Der Nutzer drückt und sieht immer noch nichts, weil ein Filter stehen blieb.
    /// </remarks>
    public static readonly DependencyProperty ResetLabelProperty = DependencyProperty.Register(
        nameof(ResetLabel),
        typeof(string),
        typeof(EmptyState),
        new PropertyMetadata("Suche zurücksetzen"));

    /// <summary>Erzeugt den Leerzustand.</summary>
    public EmptyState()
    {
        InitializeComponent();
    }

    /// <inheritdoc cref="HeadlineProperty" />
    public string Headline
    {
        get => (string)GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    /// <inheritdoc cref="HintProperty" />
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <inheritdoc cref="ShowsResetProperty" />
    public bool ShowsReset
    {
        get => (bool)GetValue(ShowsResetProperty);
        set => SetValue(ShowsResetProperty, value);
    }

    /// <inheritdoc cref="ResetCommandProperty" />
    public ICommand? ResetCommand
    {
        get => (ICommand?)GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    /// <inheritdoc cref="ResetLabelProperty" />
    public string ResetLabel
    {
        get => (string)GetValue(ResetLabelProperty);
        set => SetValue(ResetLabelProperty, value);
    }
}
