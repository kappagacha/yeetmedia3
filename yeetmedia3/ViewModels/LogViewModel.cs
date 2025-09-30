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
    private LogEntry? _selectedLog;

    public LogViewModel(LoggingService loggingService)
    {
        _loggingService = loggingService;

        ClearLogsCommand = new Command(() => _loggingService.Clear());
        CopyLogCommand = new Command<LogEntry>(async (log) => await CopyLogToClipboard(log));
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

    public ICommand ClearLogsCommand { get; }
    public ICommand CopyLogCommand { get; }

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