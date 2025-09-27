using Yeetmedia3.Services;
using Yeetmedia3.ViewModels;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Yeetmedia3.Views;

[QueryProperty(nameof(CurrentFolderId), "currentFolderId")]
[QueryProperty(nameof(FileToEdit), "fileToEdit")]
[QueryProperty(nameof(IsNewFile), "isNewFile")]
public partial class JsonEditorView : ContentPage
{
    private JsonEditorViewModel _viewModel;

    public JsonEditorView()
    {
        InitializeComponent();
        // Create a new instance of GoogleDriveService for the JSON editor
        var googleDriveService = new GoogleDriveService();
        _viewModel = new JsonEditorViewModel(googleDriveService);
        BindingContext = _viewModel;
    }

    public string CurrentFolderId { get; set; } = string.Empty;
    public DriveFile? FileToEdit { get; set; }
    public bool IsNewFile { get; set; }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (!string.IsNullOrEmpty(CurrentFolderId))
        {
            _viewModel.SetCurrentFolder(CurrentFolderId);
        }

        if (!IsNewFile && FileToEdit != null)
        {
            await _viewModel.LoadFileAsync(FileToEdit);
        }
    }
}