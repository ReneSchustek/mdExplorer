using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MdExplorer.App.ViewModels;

namespace MdExplorer.App.Views.Panels;

/// <summary>
/// Tab-Panel mit der flachen Liste aller indizierten Markdown-Dateien.
/// </summary>
/// <remarks>
/// Filtern und Sortieren bleiben Sache des gebundenen
/// <see cref="MdExplorer.App.ViewModels.AllFilesViewModel"/>. Hier steht nur, was die
/// Ansicht angeht: die sichtbare Gruppierung, das Rollen zu einer Buchstabengruppe und
/// das Bewahren des Rollstands.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed partial class AllFilesPanel : UserControl
{
    private AllFilesViewModel? _viewModel;
    private double _keptVerticalOffset;

    /// <summary>Erstellt das Panel.</summary>
    public AllFilesPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private static ScrollViewer? ScrollViewerOf(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            ScrollViewer? candidate = ScrollViewerOf(VisualTreeHelper.GetChild(root, index));
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Gruppiert die Liste sichtbar nach der Beschriftung, der auch die Sprungleiste folgt.
    /// </summary>
    /// <remarks>
    /// Ohne sichtbare Gruppen führt ein Sprung ins Nichts — man landet an einer Stelle, die
    /// sich von jeder anderen nicht unterscheidet.
    /// </remarks>
    private static void GroupByLabel(AllFilesViewModel viewModel)
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(viewModel.Items);
        if (view.GroupDescriptions is null || view.GroupDescriptions.Count > 0)
        {
            return;
        }

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AllFilesItemViewModel.GroupLabel)));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.JumpRequested -= OnJumpRequested;
        }

        _viewModel = args.NewValue as AllFilesViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.JumpRequested += OnJumpRequested;
        GroupByLabel(_viewModel);
    }

    private void OnJumpRequested(char letter)
    {
        if (_viewModel is null)
        {
            return;
        }

        string label = letter.ToString(CultureInfo.InvariantCulture);
        AllFilesItemViewModel? first = _viewModel.Items.FirstOrDefault(
            item => string.Equals(item.GroupLabel, label, StringComparison.Ordinal));

        if (first is null)
        {
            return;
        }

        // Rollen statt filtern: Der übrige Bestand bleibt sichtbar, und wer stöbert, behält
        // den Überblick. Die Auswahl bleibt unangetastet — ein Sprung ist kein Öffnen.
        ItemsList.ScrollIntoView(first);
    }

    /// <summary>
    /// Bewahrt den Rollstand über einen Registerwechsel hinweg.
    /// </summary>
    /// <remarks>
    /// Wer aus einem Dokument zurückkommt und die Liste wieder ganz oben vorfindet, sucht
    /// seine Stelle jedes Mal neu. Suche und Filter überstehen den Weg ohnehin, weil sie im
    /// ViewModel stehen; der Rollstand gehört der Ansicht und geht sonst verloren.
    /// </remarks>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        ScrollViewer? scrollViewer = ScrollViewerOf(ItemsList);
        if (scrollViewer is null)
        {
            return;
        }

        if (args.NewValue is false)
        {
            _keptVerticalOffset = scrollViewer.VerticalOffset;
            return;
        }

        scrollViewer.ScrollToVerticalOffset(_keptVerticalOffset);
    }
}
