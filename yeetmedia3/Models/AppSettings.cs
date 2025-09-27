namespace Yeetmedia3.Models;

public class AppSettings
{
    public GoogleDriveSettings GoogleDrive { get; set; } = new GoogleDriveSettings();
}

public class GoogleDriveSettings
{
    public string AndroidClientId { get; set; } = string.Empty;
    public string WindowsClientId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
}