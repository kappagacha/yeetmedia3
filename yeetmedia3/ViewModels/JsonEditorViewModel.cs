using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Yeetmedia3.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Yeetmedia3.ViewModels;

public class JsonEditorViewModel : INotifyPropertyChanged
{
    private readonly GoogleDriveService _googleDriveService;
    private string _fileName = "untitled.json";
    private string _jsonContent = "{\n  \n}";
    private string _statusMessage = "Ready";
    private string _statusColor = "Gray";
    private string _currentFileId = string.Empty;
    private string _currentFolderId = string.Empty;
    private bool _isReadOnly;

    public JsonEditorViewModel(GoogleDriveService googleDriveService)
    {
        _googleDriveService = googleDriveService;

        // Initialize commands
        ValidateJsonCommand = new Command(ValidateJson, () => !IsReadOnly);
        FormatJsonCommand = new Command(FormatJson, () => !IsReadOnly);
        SaveToDriveCommand = new Command(async () => await SaveToDriveAsync(), () => !IsReadOnly);
        NewFileCommand = new Command(CreateNewFile, () => !IsReadOnly);
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public string JsonContent
    {
        get => _jsonContent;
        set
        {
            _jsonContent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineCount));
            OnPropertyChanged(nameof(CharCount));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusColor
    {
        get => _statusColor;
        set
        {
            _statusColor = value;
            OnPropertyChanged();
        }
    }

    public int LineCount => string.IsNullOrEmpty(JsonContent) ? 0 : JsonContent.Split('\n').Length;
    public int CharCount => string.IsNullOrEmpty(JsonContent) ? 0 : JsonContent.Length;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            OnPropertyChanged();
            // Update command executability
            ((Command)ValidateJsonCommand).ChangeCanExecute();
            ((Command)FormatJsonCommand).ChangeCanExecute();
            ((Command)SaveToDriveCommand).ChangeCanExecute();
            ((Command)NewFileCommand).ChangeCanExecute();
        }
    }

    public ICommand ValidateJsonCommand { get; }
    public ICommand FormatJsonCommand { get; }
    public ICommand SaveToDriveCommand { get; }
    public ICommand NewFileCommand { get; }

    private void ValidateJson()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(JsonContent))
            {
                StatusMessage = "JSON is empty";
                StatusColor = "Orange";
                return;
            }

            var jsonDoc = JsonDocument.Parse(JsonContent);
            jsonDoc.Dispose();
            StatusMessage = "Valid JSON ✓";
            StatusColor = "Green";
        }
        catch (JsonException ex)
        {
            StatusMessage = $"Invalid JSON: {ex.Message}";
            StatusColor = "Red";
        }
    }

    private void FormatJson()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(JsonContent))
            {
                StatusMessage = "JSON is empty";
                StatusColor = "Orange";
                return;
            }

            var jsonDoc = JsonDocument.Parse(JsonContent);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            JsonContent = JsonSerializer.Serialize(jsonDoc.RootElement, options);
            jsonDoc.Dispose();
            StatusMessage = "JSON formatted ✓";
            StatusColor = "Green";
        }
        catch (JsonException ex)
        {
            StatusMessage = $"Cannot format: {ex.Message}";
            StatusColor = "Red";
        }
    }

    private async Task SaveToDriveAsync()
    {
        try
        {
            // Validate JSON first
            ValidateJson();
            if (StatusColor == "Red")
            {
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlert("Error", "Please fix JSON errors before saving", "OK");
                }
                return;
            }

            // Ensure file name has .json extension
            if (!FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                FileName += ".json";
            }

            StatusMessage = "Saving to Google Drive...";
            StatusColor = "Blue";

            // Convert content to stream
            var contentBytes = Encoding.UTF8.GetBytes(JsonContent);
            using var stream = new MemoryStream(contentBytes);

            if (string.IsNullOrEmpty(_currentFileId))
            {
                // Create new file
                var fileId = await _googleDriveService.UploadFileAsync(
                    FileName,
                    stream,
                    "application/json",
                    _currentFolderId);

                _currentFileId = fileId;
                StatusMessage = "File created successfully ✓";
                StatusColor = "Green";
            }
            else
            {
                // Update existing file
                await _googleDriveService.UpdateFileAsync(
                    _currentFileId,
                    stream,
                    "application/json");

                StatusMessage = "File updated successfully ✓";
                StatusColor = "Green";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            StatusColor = "Red";
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", $"Failed to save: {ex.Message}", "OK");
            }
        }
    }

    private void CreateNewFile()
    {
        FileName = "untitled.json";
        JsonContent = "{\n  \n}";
        _currentFileId = string.Empty;
        StatusMessage = "New file created";
        StatusColor = "Gray";
    }

    public async Task LoadFileAsync(DriveFile file, bool readOnly = false)
    {
        try
        {
            StatusMessage = "Loading file...";
            StatusColor = "Blue";

            FileName = file.Name;
            _currentFileId = file.Id;
            IsReadOnly = readOnly;

            // Download file content
            using var stream = await _googleDriveService.DownloadFileAsync(file.Id);
            using var reader = new StreamReader(stream);
            JsonContent = await reader.ReadToEndAsync();

            // Format it for better readability
            FormatJson();

            StatusMessage = readOnly ? "File opened in read-only mode" : "File loaded successfully ✓";
            StatusColor = readOnly ? "Orange" : "Green";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load file: {ex.Message}";
            StatusColor = "Red";
        }
    }

    public void SetCurrentFolder(string folderId)
    {
        _currentFolderId = folderId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}