using System.Windows;
using MdExplorer.TagCloud.ViewModels;

namespace MdExplorer.TagCloud.Views;

/// <summary>
/// Code-Behind des Tag-Management-Fensters. Bindet das ViewModel und triggert beim
/// Oeffnen einen initialen <see cref="TagManagementViewModel.RefreshAsync"/>-Aufruf,
/// damit die Liste sofort befuellt wird.
/// </summary>
public sealed partial class TagManagementWindow : Window
{
    private readonly TagManagementViewModel _viewModel;

    /// <summary>Erzeugt das Fenster und bindet das ViewModel.</summary>
    public TagManagementWindow(TagManagementViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await _viewModel.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
