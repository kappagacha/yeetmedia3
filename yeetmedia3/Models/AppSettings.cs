namespace Yeetmedia3.Models;

public class AppSettings
{
    public GoogleDriveSettings GoogleDrive { get; set; }
}

public class GoogleDriveSettings
{
    public string AndroidClientId { get; set; }
    public string WindowsClientId { get; set; }
    public string ApplicationName { get; set; }
}