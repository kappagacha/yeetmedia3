using SQLite;

namespace Yeetmedia3.Models;

public class LogEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }

    [Ignore]
    public string LevelColor => Level switch
    {
        LogLevel.Error => "#DC3545",
        LogLevel.Warning => "#FFC107",
        LogLevel.Info => "#17A2B8",
        LogLevel.Debug => "#6C757D",
        _ => "#000000"
    };

    [Ignore]
    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}