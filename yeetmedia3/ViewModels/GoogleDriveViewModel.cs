using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yeetmedia3.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Yeetmedia3.ViewModels;

public class GoogleDriveViewModel : INotifyPropertyChanged
{
    private readonly GoogleDriveService _googleDriveService;
    private readonly LoggingService _loggingService;
    private bool _isLoading;
    private bool _isAuthenticated;
    private string _errorMessage = string.Empty;
    private ObservableCollection<DriveFile> _files = new ObservableCollection<DriveFile>();
    private DriveFile? _selectedFile;
    private string _currentFolderId = "root";
    private string _currentFolderName = "My Drive";
    private Stack<(string Id, string Name)> _navigationStack = new Stack<(string, string)>();

    public GoogleDriveViewModel(GoogleDriveService googleDriveService, LoggingService loggingService)
    {
        _googleDriveService = googleDriveService;
        _loggingService = loggingService;
        Files = new ObservableCollection<DriveFile>();

        // Initialize commands
        AuthenticateCommand = new Command(async () => await AuthenticateAsync());
        RefreshFilesCommand = new Command(async () => await RefreshFilesAsync(), () => IsAuthenticated);
        LoadFoldersCommand = new Command(async () => await LoadFoldersAsync(), () => IsAuthenticated);
        LoadFilesInFolderCommand = new Command<string>(async (folderId) => await LoadFilesInFolderAsync(folderId), (folderId) => IsAuthenticated && !string.IsNullOrEmpty(folderId));
        SearchFilesCommand = new Command<string>(async (searchTerm) => await SearchFilesAsync(searchTerm), (searchTerm) => IsAuthenticated && !string.IsNullOrEmpty(searchTerm));
        DownloadFileCommand = new Command<DriveFile>(async (file) => await DownloadFileAsync(file), (file) => IsAuthenticated && file != null);
        OpenItemCommand = new Command<DriveFile>(async (file) => await OpenItemAsync(file), (file) => IsAuthenticated && file != null);
        GoBackCommand = new Command(async () => await GoBackAsync(), () => CanGoBack);
        SignOutCommand = new Command(async () => await SignOutAsync(), () => IsAuthenticated);
        DeleteFileCommand = new Command<DriveFile>(async (file) => await DeleteFileAsync(file), (file) => IsAuthenticated && file != null);
        CopyFileContentsCommand = new Command<DriveFile>(async (file) => await CopyFileContentsAsync(file), (file) => CanCopyFileContents(file));

        // Initialize JSON commands
        NewJsonFileCommand = new Command(async () => await NewJsonFileAsync());
        EditJsonFileCommand = new Command<DriveFile>(async (file) => await EditJsonFileAsync(file));

        // Try to auto-sign in with cached credentials
        Task.Run(async () => await TryAutoSignInAsync());
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            _isAuthenticated = value;
            OnPropertyChanged();
            ((Command)RefreshFilesCommand).ChangeCanExecute();
            ((Command)LoadFoldersCommand).ChangeCanExecute();
            ((Command)SignOutCommand).ChangeCanExecute();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<DriveFile> Files
    {
        get => _files;
        set
        {
            _files = value;
            OnPropertyChanged();
        }
    }

    public DriveFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            OnPropertyChanged();
        }
    }

    public string CurrentFolderName
    {
        get => _currentFolderName;
        set
        {
            _currentFolderName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    public bool CanGoBack => _navigationStack.Count > 0;

    public ICommand AuthenticateCommand { get; }
    public ICommand RefreshFilesCommand { get; }
    public ICommand LoadFoldersCommand { get; }
    public ICommand LoadFilesInFolderCommand { get; }
    public ICommand SearchFilesCommand { get; }
    public ICommand DownloadFileCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand OpenItemCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand NewJsonFileCommand { get; }
    public ICommand EditJsonFileCommand { get; }
    public ICommand DeleteFileCommand { get; }
    public ICommand CopyFileContentsCommand { get; }

    private async Task AuthenticateAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            await _googleDriveService.InitializeAsync();
            IsAuthenticated = await _googleDriveService.IsAuthenticatedAsync();

            if (IsAuthenticated)
            {
                await RefreshFilesAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Authentication failed: {ex.Message}";
            IsAuthenticated = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshFilesAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            // List files and folders in current folder
            var query = $"'{_currentFolderId}' in parents and trashed = false";
            var files = await _googleDriveService.ListFilesAsync(
                pageSize: 50,
                query: query);
            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(file);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load files: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFoldersAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var folders = await _googleDriveService.ListFoldersAsync();
            Files.Clear();
            foreach (var folder in folders)
            {
                Files.Add(folder);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load folders: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFilesInFolderAsync(string folderId)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var files = await _googleDriveService.ListFilesInFolderAsync(folderId);
            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(file);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load files in folder: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SearchFilesAsync(string searchTerm)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var files = await _googleDriveService.SearchFilesAsync(searchTerm);
            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(file);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DownloadFileAsync(DriveFile file)
    {
        if (file == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            using var stream = await _googleDriveService.DownloadFileAsync(file.Id);

            // Save to local storage
            var localPath = Path.Combine(FileSystem.CacheDirectory, file.Name);
            using var fileStream = System.IO.File.Create(localPath);
            await stream.CopyToAsync(fileStream);

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Success", $"File downloaded to: {localPath}", "OK");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Download failed: {ex.Message}";
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", ErrorMessage, "OK");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SignOutAsync()
    {
        try
        {
            await _googleDriveService.SignOutAsync();
            IsAuthenticated = false;
            Files.Clear();
            SelectedFile = null;
            ErrorMessage = string.Empty;
            _currentFolderId = "root";
            _currentFolderName = "My Drive";
            _navigationStack.Clear();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sign out failed: {ex.Message}";
        }
    }

    private async Task OpenItemAsync(DriveFile file)
    {
        if (file == null) return;

        // Check if it's a folder
        if (file.MimeType == "application/vnd.google-apps.folder")
        {
            // Save current folder to navigation stack
            _navigationStack.Push((_currentFolderId, _currentFolderName));

            // Navigate to the folder
            _currentFolderId = file.Id;
            CurrentFolderName = file.Name;

            // Refresh files for the new folder
            await RefreshFilesAsync();

            // Update GoBack command state
            ((Command)GoBackCommand).ChangeCanExecute();
        }
        // Check if it's a JSON file - open in read-only mode
        else if (file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var navigationParameter = new Dictionary<string, object>
            {
                { "currentFolderId", _currentFolderId },
                { "fileToEdit", file },
                { "isNewFile", false },
                { "isReadOnly", true }
            };
            await Shell.Current.GoToAsync(nameof(Views.JsonEditorView), navigationParameter);
        }
        else
        {
            // For other files, download them
            await DownloadFileAsync(file);
        }
    }

    private async Task GoBackAsync()
    {
        if (_navigationStack.Count > 0)
        {
            var (parentId, parentName) = _navigationStack.Pop();
            _currentFolderId = parentId;
            CurrentFolderName = parentName;

            await RefreshFilesAsync();

            // Update GoBack command state
            ((Command)GoBackCommand).ChangeCanExecute();
        }
    }

    private async Task NewJsonFileAsync()
    {
        try
        {
            var navigationParameter = new Dictionary<string, object>
            {
                { "currentFolderId", _currentFolderId },
                { "isNewFile", true }
            };

            await Shell.Current.GoToAsync(nameof(Views.JsonEditorView), navigationParameter);
        }
        catch (Exception ex)
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", $"Failed to open JSON editor: {ex.Message}", "OK");
            }
        }
    }

    private async Task EditJsonFileAsync(DriveFile file)
    {
        if (file == null || !file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", "Please select a JSON file to edit", "OK");
            }
            return;
        }

        var navigationParameter = new Dictionary<string, object>
        {
            { "currentFolderId", _currentFolderId },
            { "fileToEdit", file },
            { "isNewFile", false },
            { "isReadOnly", false }
        };
        await Shell.Current.GoToAsync(nameof(Views.JsonEditorView), navigationParameter);
    }

    private bool CanCopyFileContents(DriveFile? file)
    {
        if (file == null) return false;

        // Only allow copying contents for JSON and TXT files
        var fileName = file.Name.ToLower();
        return fileName.EndsWith(".json") || fileName.EndsWith(".txt");
    }

    private async Task DeleteFileAsync(DriveFile file)
    {
        if (file == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                var confirmed = await window.Page.DisplayAlert(
                    "Confirm Delete",
                    $"Are you sure you want to delete '{file.Name}'?",
                    "Delete",
                    "Cancel");

                if (!confirmed)
                {
                    return;
                }
            }

            await _googleDriveService.DeleteFileAsync(file.Id);
            _loggingService.Info("GoogleDrive", $"File deleted: {file.Name}");

            // Refresh the file list
            await RefreshFilesAsync();
        }
        catch (Exception ex)
        {
            _loggingService.Error("GoogleDrive", $"Failed to delete file: {file.Name}", ex);
            ErrorMessage = $"Delete failed: {ex.Message}";

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", $"Failed to delete file:\n{ex.Message}", "OK");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CopyFileContentsAsync(DriveFile file)
    {
        if (file == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            // Download file content
            using var stream = await _googleDriveService.DownloadFileAsync(file.Id);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            // Add filename as first line
            var clipboardContent = $"File: {file.Name}\n{content}";

            // Copy to clipboard
            await Clipboard.Default.SetTextAsync(clipboardContent);

            _loggingService.Info("GoogleDrive", $"Copied contents to clipboard: {file.Name}");

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Success", $"Copied contents of '{file.Name}' to clipboard.", "OK");
            }
        }
        catch (Exception ex)
        {
            _loggingService.Error("GoogleDrive", $"Failed to copy file contents: {file.Name}", ex);
            ErrorMessage = $"Copy failed: {ex.Message}";

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", $"Failed to copy file contents:\n{ex.Message}", "OK");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task TryAutoSignInAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[GoogleDriveViewModel] TryAutoSignInAsync starting...");

            // Wait a moment for UI to be ready
            await Task.Delay(500);

            // Update UI on main thread
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                IsLoading = true;
                ErrorMessage = "Checking for cached credentials...";

                try
                {
                    // Try to initialize with cached credentials
                    await _googleDriveService.InitializeAsync();

                    // Check if we're authenticated after initialization
                    var isAuth = await _googleDriveService.IsAuthenticatedAsync();
                    System.Diagnostics.Debug.WriteLine($"[GoogleDriveViewModel] Auto sign-in check: {isAuth}");

                    if (isAuth)
                    {
                        System.Diagnostics.Debug.WriteLine("[GoogleDriveViewModel] Auto sign-in successful");
                        IsAuthenticated = true;
                        ErrorMessage = string.Empty;
                        await RefreshFilesAsync();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[GoogleDriveViewModel] No cached credentials found");
                        ErrorMessage = string.Empty;
                        IsAuthenticated = false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GoogleDriveViewModel] Auto sign-in error: {ex.Message}");
                    ErrorMessage = string.Empty;
                    IsAuthenticated = false;
                }
                finally
                {
                    IsLoading = false;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleDriveViewModel] TryAutoSignInAsync failed: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}