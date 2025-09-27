using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class DotnetRocksView : ContentPage
{
    public DotnetRocksView(DotnetRocksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnNavigateToGoogleDrive(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(GoogleDriveView));
    }
}