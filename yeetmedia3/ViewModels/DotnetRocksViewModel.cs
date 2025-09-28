using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yeetmedia3.Services;

namespace Yeetmedia3.ViewModels;

public class DotnetRocksViewModel : INotifyPropertyChanged
{
    private readonly DotnetRocksService _dotnetRocksService;
    private readonly GoogleDriveService _googleDriveService;

    private int _episodeNumber;
    private string _episodeTitle = string.Empty;
    private string _episodeDescription = string.Empty;
    private string _episodeUrl = string.Empty;
    private DateTime? _episodePublishDate;
    private bool _isLoading;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusMessage = string.Empty;
    private string _downloadedFilePath = string.Empty;
    private bool _canDownload;
    private bool _isEpisodeCached;

    public DotnetRocksViewModel(DotnetRocksService dotnetRocksService, GoogleDriveService googleDriveService)
    {
        _dotnetRocksService = dotnetRocksService;
        _googleDriveService = googleDriveService;

        // Initialize commands
        DownloadEpisodeCommand = new Command(async () =>
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Download command executed. IsDownloading: {IsDownloading}");
            await DownloadEpisodeAsync();
        }, () => !IsDownloading);
        PlayEpisodeCommand = new Command(async () => await PlayEpisodeAsync(), () => IsEpisodeCached);
        ShowActionsCommand = new Command(async () => await ShowActionsAsync());

        // Set default episode number
        EpisodeNumber = 1001; // Default episode
    }

    public int EpisodeNumber
    {
        get => _episodeNumber;
        set
        {
            _episodeNumber = value;
            OnPropertyChanged();
            CheckIfCached();
        }
    }

    public string EpisodeTitle
    {
        get => _episodeTitle;
        set
        {
            _episodeTitle = value;
            OnPropertyChanged();
        }
    }

    public string EpisodeDescription
    {
        get => _episodeDescription;
        set
        {
            _episodeDescription = value;
            OnPropertyChanged();
        }
    }

    public string EpisodeUrl
    {
        get => _episodeUrl;
        set
        {
            _episodeUrl = value;
            OnPropertyChanged();
        }
    }

    public DateTime? EpisodePublishDate
    {
        get => _episodePublishDate;
        set
        {
            _episodePublishDate = value;
            OnPropertyChanged();
        }
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

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            _isDownloading = value;
            OnPropertyChanged();
            ((Command)DownloadEpisodeCommand).ChangeCanExecute();
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            _downloadProgress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DownloadProgressPercent));
        }
    }

    public string DownloadProgressPercent => $"{(int)(DownloadProgress * 100)}%";

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string DownloadedFilePath
    {
        get => _downloadedFilePath;
        set
        {
            _downloadedFilePath = value;
            OnPropertyChanged();
        }
    }

    public bool CanDownload
    {
        get => _canDownload;
        set
        {
            _canDownload = value;
            OnPropertyChanged();
            ((Command)DownloadEpisodeCommand).ChangeCanExecute();
        }
    }

    public bool IsEpisodeCached
    {
        get => _isEpisodeCached;
        set
        {
            _isEpisodeCached = value;
            OnPropertyChanged();
            ((Command)PlayEpisodeCommand).ChangeCanExecute();
        }
    }

    public ICommand DownloadEpisodeCommand { get; }
    public ICommand PlayEpisodeCommand { get; }
    public ICommand ShowActionsCommand { get; }


    private async Task DownloadEpisodeAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Starting download for episode {EpisodeNumber}");
            IsDownloading = true;
            IsLoading = true;
            DownloadProgress = 0;
            StatusMessage = $"Getting info for episode {EpisodeNumber}...";

            // First get episode info (which will extract the audio URL using WebView)
            var episode = await _dotnetRocksService.GetEpisodeInfoAsync(EpisodeNumber);

            EpisodeTitle = episode.Title;
            EpisodeDescription = episode.Description;
            EpisodeUrl = episode.AudioUrl;
            EpisodePublishDate = episode.PublishDate;

            if (string.IsNullOrEmpty(episode.AudioUrl))
            {
                StatusMessage = $"Could not find download URL for episode {EpisodeNumber}";
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window?.Page != null)
                {
                    await window.Page.DisplayAlert("Download Error",
                        $"Could not find audio URL for episode {EpisodeNumber}", "OK");
                }
                return;
            }

            StatusMessage = $"Downloading episode {EpisodeNumber}...";
            IsLoading = false;

            var lastReportedProgress = -1;
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusMessage = $"Downloading: {DownloadProgressPercent}";

                // Only log every 10% to reduce spam
                var currentProgress = (int)(p * 100);
                if (currentProgress >= lastReportedProgress + 10)
                {
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Download progress: {currentProgress}%");
                    lastReportedProgress = currentProgress;
                }
            });

            var filePath = await _dotnetRocksService.DownloadEpisodeAsync(EpisodeNumber, progress);

            DownloadedFilePath = filePath;
            StatusMessage = $"Downloaded successfully to: {filePath}";
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Downloaded to: {filePath}");
            CheckIfCached();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Download failed: {ex}");
            StatusMessage = $"Download failed: {ex.Message}";
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Download Error", ex.Message, "OK");
            }
        }
        finally
        {
            IsDownloading = false;
            IsLoading = false;
            DownloadProgress = 0;
        }
    }


    private async Task PlayEpisodeAsync()
    {
        try
        {
            var filePath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
            if (!string.IsNullOrEmpty(filePath))
            {
                // Open the file with the default audio player
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(filePath)
                });
            }
            else
            {
                StatusMessage = "Episode not found in cache";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to play episode: {ex.Message}";
        }
    }

    private async Task ShowActionsAsync()
    {
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page == null) return;

            var action = await window.Page.DisplayActionSheet(
                "Choose Action",
                "Cancel",
                null,
                "Open Downloads Folder",
                "Clear All Cache");

            if (action == "Open Downloads Folder")
            {
                await OpenDownloadsFolderAsync();
            }
            else if (action == "Clear All Cache")
            {
                // Show confirmation dialog for clearing cache
                var confirm = await window.Page.DisplayAlert(
                    "Clear Cache",
                    "Are you sure you want to clear all downloaded episodes?",
                    "Yes",
                    "No");

                if (confirm)
                {
                    ClearCache();
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Action failed: {ex.Message}";
        }
    }

    private void ClearCache()
    {
        try
        {
            _dotnetRocksService.ClearCache();
            StatusMessage = "Cache cleared successfully";
            CheckIfCached();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to clear cache: {ex.Message}";
        }
    }

    private void CheckIfCached()
    {
        var cachedPath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
        IsEpisodeCached = !string.IsNullOrEmpty(cachedPath);

        if (IsEpisodeCached)
        {
            DownloadedFilePath = cachedPath ?? string.Empty;
        }
    }

    private async Task OpenDownloadsFolderAsync()
    {
        try
        {
            var downloadPath = Path.Combine(FileSystem.CacheDirectory, "dotnetrocks", "podcasts");

            // Ensure the directory exists
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

#if WINDOWS
            // On Windows, open Explorer to the folder
            Process.Start("explorer.exe", downloadPath);
            StatusMessage = "Opened downloads folder";
#elif ANDROID
            // On Android, show a message with the path since we can't directly open file explorer
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Downloads Location",
                    $"Episodes are saved to:\n{downloadPath}\n\nYou can access this from your file manager app.", "OK");
            }
            StatusMessage = "Downloads path shown";
#else
            // For other platforms, try to open with the default handler
            await Launcher.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(downloadPath)
            });
            StatusMessage = "Opened downloads folder";
#endif
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open folder: {ex.Message}";
            // Show the path as a fallback
            var downloadPath = Path.Combine(FileSystem.CacheDirectory, "dotnetrocks", "podcasts");
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Downloads Location",
                    $"Episodes are saved to:\n{downloadPath}", "OK");
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}