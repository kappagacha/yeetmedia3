using CommunityToolkit.Maui.Views;
using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class DotnetRocksView : ContentPage
{
    private DotnetRocksViewModel _viewModel;
    private bool _isInitialized = false;

    public DotnetRocksView(DotnetRocksViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        System.Diagnostics.Debug.WriteLine($"[DotnetRocksView] OnDisappearing - Navigating away from view");

        // Save state when page disappears (app backgrounded or navigating away)
        // Don't pause - let it keep playing in background
        await _viewModel.SaveStateOnBackgroundAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        System.Diagnostics.Debug.WriteLine($"[DotnetRocksView] OnAppearing - View is appearing");

        if (MediaPlayer != null)
        {
            // Only initialize MediaElement once (subscribe to events)
            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksView] Initializing MediaElement");
                _viewModel.SetMediaElement(MediaPlayer);
                _isInitialized = true;
            }

            // Always check if episode needs to be loaded/restored when view appears
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksView] Checking if episode needs to be loaded/restored");
            await _viewModel.CheckAndReloadEpisodeAsync();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksView] MediaPlayer is null");
        }
    }
}