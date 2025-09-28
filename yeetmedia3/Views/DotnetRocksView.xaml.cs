using CommunityToolkit.Maui.Views;
using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class DotnetRocksView : ContentPage
{
    private DotnetRocksViewModel _viewModel;

    public DotnetRocksView(DotnetRocksViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Pass the MediaElement to the ViewModel
        _viewModel.SetMediaElement(MediaPlayer);
    }
}