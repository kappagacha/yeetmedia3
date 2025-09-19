using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class GoogleDriveView : ContentPage
{
    private GoogleDriveViewModel _viewModel;

    public GoogleDriveView(GoogleDriveViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async void OnNewJsonButtonClicked(object sender, EventArgs e)
    {
        try
        {
            // Use the ViewModel's command directly
            if (_viewModel.NewJsonFileCommand.CanExecute(null))
            {
                _viewModel.NewJsonFileCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to create new JSON file: {ex.Message}", "OK");
        }
    }
}