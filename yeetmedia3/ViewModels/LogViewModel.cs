using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yeetmedia3.Models;
using Yeetmedia3.Services;

namespace Yeetmedia3.ViewModels;

public class LogViewModel : INotifyPropertyChanged
{
    private readonly LoggingService _loggingService;
    private readonly GoogleDriveService _googleDriveService;
    private LogEntry? _selectedLog;
    private bool _isExporting;

    public LogViewModel(LoggingService loggingService, GoogleDriveService googleDriveService)
    {
        _loggingService = loggingService;
        _googleDriveService = googleDriveService;

        ClearLogsCommand = new Command(() => _loggingService.Clear());
        CopyLogCommand = new Command<LogEntry>(async (log) => await CopyLogToClipboard(log));
        ExportLogsCommand = new Command(async () => await ExportLogsAsync(), () => !IsExporting);
    }

    public ObservableCollection<LogEntry> Logs => _loggingService.Logs;

    public LogEntry? SelectedLog
    {
        get => _selectedLog;
        set
        {
            _selectedLog = value;
            OnPropertyChanged();
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        set
        {
            _isExporting = value;
            OnPropertyChanged();
            ((Command)ExportLogsCommand).ChangeCanExecute();
        }
    }

    public ICommand ClearLogsCommand { get; }
    public ICommand CopyLogCommand { get; }
    public ICommand ExportLogsCommand { get; }

    private async Task ExportLogsAsync()
    {
        var window = Application.Current?.Windows.FirstOrDefault();

        try
        {
            IsExporting = true;

            // Check if authenticated
            await _googleDriveService.InitializeAsync();
            var isAuthenticated = await _googleDriveService.IsAuthenticatedAsync();

            if (!isAuthenticated)
            {
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlert("Not Authenticated", "Please sign in to Google Drive first to export logs.", "OK");
                }
                return;
            }

            // Find or create the applogs folder
            const string folderName = "applogs";
            var folderQuery = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var folders = await _googleDriveService.ListFilesAsync(10, folderQuery);
            var folder = folders?.FirstOrDefault();

            string? folderId = null;
            if (folder == null)
            {
                // Create the applogs folder
                folderId = await _googleDriveService.CreateFolderAsync(folderName);
                _loggingService.Info("Logs", $"Created applogs folder: {folderId}");
            }
            else
            {
                folderId = folder.Id;
            }

            // Export logs from LoggingService
            using var logStream = await _loggingService.ExportLogsAsync();

            // Create filename with timestamp and device name
            var deviceName = DeviceInfo.Current.Name.Replace(" ", "_").Replace(".", "");
            var fileName = $"yeetmedia3_logs_{deviceName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            // Upload to Google Drive in the applogs folder
            var fileId = await _googleDriveService.UploadFileAsync(
                fileName,
                logStream,
                "application/json",
                folderId);

            _loggingService.Info("Logs", $"Logs exported to Google Drive: {fileName}");

            // Show success message
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Success", $"Logs exported to Google Drive:\n{fileName}", "OK");
            }
        }
        catch (Exception ex)
        {
            _loggingService.Error("Logs", "Failed to export logs to Google Drive", ex);

            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Error", $"Failed to export logs:\n{ex.Message}", "OK");
            }
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task CopyLogToClipboard(LogEntry? log)
    {
        if (log == null) return;

        var text = $"[{log.FormattedTimestamp}] [{log.Level}] [{log.Category}] {log.Message}";
        if (!string.IsNullOrEmpty(log.Exception))
        {
            text += $"\n{log.Exception}";
        }

        await Clipboard.Default.SetTextAsync(text);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}