using System.Collections.ObjectModel;
using SQLite;
using Yeetmedia3.Models;

namespace Yeetmedia3.Services;

public class LoggingService
{
    private readonly ObservableCollection<LogEntry> _logs = new();
    private readonly int _maxLogEntries = 500;
    private readonly SQLiteAsyncConnection _database;

    public ObservableCollection<LogEntry> Logs => _logs;

    public LoggingService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "logs.db");
        _database = new SQLiteAsyncConnection(dbPath);
        _ = InitializeDatabaseAsync();
    }

    private async Task InitializeDatabaseAsync()
    {
        await _database.CreateTableAsync<LogEntry>();
        await LoadLogsFromDatabaseAsync();
    }

    private async Task LoadLogsFromDatabaseAsync()
    {
        try
        {
            var logs = await _database.Table<LogEntry>()
                .OrderByDescending(l => l.Timestamp)
                .Take(_maxLogEntries)
                .ToListAsync();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _logs.Clear();
                foreach (var log in logs)
                {
                    _logs.Add(log);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoggingService] Failed to load logs from database: {ex.Message}");
        }
    }

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message,
                Exception = exception?.ToString()
            };

            _logs.Insert(0, logEntry);

            // Limit the number of log entries in memory
            while (_logs.Count > _maxLogEntries)
            {
                _logs.RemoveAt(_logs.Count - 1);
            }

            // Save to database
            try
            {
                await _database.InsertAsync(logEntry);

                // Clean up old entries from database (keep last 1000)
                var oldEntry = await _database.Table<LogEntry>()
                    .OrderByDescending(l => l.Timestamp)
                    .Skip(1000)
                    .FirstOrDefaultAsync();

                if (oldEntry != null)
                {
                    await _database.ExecuteAsync("DELETE FROM LogEntry WHERE Timestamp < ?", oldEntry.Timestamp);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoggingService] Failed to save log to database: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine($"[{level}] [{category}] {message}");
        });
    }

    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    public void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public void Warning(string category, string message) => Log(LogLevel.Warning, category, message);
    public void Error(string category, string message, Exception? exception = null) => Log(LogLevel.Error, category, message, exception);

    public void Clear()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _logs.Clear();
            try
            {
                await _database.DeleteAllAsync<LogEntry>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoggingService] Failed to clear database: {ex.Message}");
            }
        });
    }
}