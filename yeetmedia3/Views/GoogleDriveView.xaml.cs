using Yeetmedia3.ViewModels;
using DriveFile = Google.Apis.Drive.v3.Data.File;

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

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is DriveFile selectedFile)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleDriveView] File selected: {selectedFile.Name}");
            _viewModel.SelectedFile = selectedFile;
        }
    }

    private void OnItemTapped(object sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is DriveFile file)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleDriveView] Item tapped: {file.Name}");
            // Manually set the selected item when tapped
            _viewModel.SelectedFile = file;
        }
    }

    private async void OnItemDoubleTapped(object sender, EventArgs e)
    {
        if (sender is Element element && element.BindingContext is DriveFile file)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleDriveView] Item double-tapped: {file.Name} (Type: {file.MimeType})");

            // Execute the OpenItemCommand with the file
            if (_viewModel.OpenItemCommand.CanExecute(file))
            {
                await Task.Run(() => _viewModel.OpenItemCommand.Execute(file));
            }
        }
    }
}