using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Yeetmedia3.Models;
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
    private bool _isBusy;
    private double _downloadProgress;
    private string _statusMessage = string.Empty;
    private string _downloadedFilePath = string.Empty;
    private bool _isEpisodeCached;

    // Media player properties
    private MediaElement? _mediaElement;
    private bool _isAutoAdvancing = false;

    // Playback tracking
    private PlaybackState? _lastSavedState;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private System.Threading.Timer? _periodicSaveTimer;
    private readonly TimeSpan _minimumSaveInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _periodicSaveInterval = TimeSpan.FromSeconds(30);
    private bool _isRestoringPosition = false;
    private bool _isLoadingEpisode = false;
    private bool _isInitializing = true; // Prevent saving during startup
    private System.Threading.Timer? _debouncedSaveTimer;
    private readonly TimeSpan _saveDebounceDelay = TimeSpan.FromSeconds(1);
    private string _lastSaveReason = "unknown";

    private readonly LoggingService _loggingService;
    private bool _hasPendingSave = false;

    public DotnetRocksViewModel(DotnetRocksService dotnetRocksService, GoogleDriveService googleDriveService, LoggingService loggingService)
    {
        _dotnetRocksService = dotnetRocksService;
        _googleDriveService = googleDriveService;
        _loggingService = loggingService;

        // Initialize commands
        DownloadEpisodeCommand = new Command(async () =>
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Download command executed. IsBusy: {IsBusy}");
            await DownloadEpisodeAsync();
        }, () => !IsBusy);
        ShowActionsCommand = new Command(async () => await ShowActionsAsync());
        IncrementEpisodeCommand = new Command(() => EpisodeNumber++);
        DecrementEpisodeCommand = new Command(() => { if (EpisodeNumber > 1) EpisodeNumber--; });
        PlayEpisodeCommand = new Command(async () => await PlayEpisodeAsync(), () => !IsBusy && !IsEpisodeCached);

        // Set default episode number
        EpisodeNumber = 1001; // Default episode

        // Monitor connectivity changes
        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        // Load playback state on startup
        _ = LoadPlaybackStateAsync();
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Connectivity changed: {e.NetworkAccess}");
        _loggingService.Info("Connectivity", $"Network access changed to: {e.NetworkAccess}");

        // If we have internet and a pending save, try to save now
        if (e.NetworkAccess == NetworkAccess.Internet && _hasPendingSave)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Internet restored, retrying pending save");
            _loggingService.Info("PlaybackState", "Internet restored, retrying pending save");
            await SavePlaybackStateAsync(force: true, reason: "connectivity restored");
        }
    }

    public int EpisodeNumber
    {
        get => _episodeNumber;
        set
        {
            if (_episodeNumber != value)
            {
                // Cancel any pending debounced save from the previous episode
                _debouncedSaveTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                _episodeNumber = value;
                OnPropertyChanged();
                CheckIfCached();
            }
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

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            ((Command)DownloadEpisodeCommand).ChangeCanExecute();
            ((Command)PlayEpisodeCommand).ChangeCanExecute();
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


    public bool IsEpisodeCached
    {
        get => _isEpisodeCached;
        set
        {
            _isEpisodeCached = value;
            OnPropertyChanged();
        }
    }

    public ICommand DownloadEpisodeCommand { get; }
    public ICommand ShowActionsCommand { get; }
    public ICommand IncrementEpisodeCommand { get; }
    public ICommand DecrementEpisodeCommand { get; }
    public ICommand PlayEpisodeCommand { get; }


    private async Task DownloadEpisodeAsync()
    {
        var startTime = DateTime.Now;
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Starting download for episode {EpisodeNumber}");
            IsBusy = true;
            DownloadProgress = 0;
            StatusMessage = $"Checking for episode {EpisodeNumber}...";

            // First, check if episode exists in Google Drive
            bool downloadedFromDrive = false;
            try
            {
                var driveProgress = new Progress<double>(p =>
                {
                    DownloadProgress = p;
                    StatusMessage = $"Downloading from Google Drive: {DownloadProgressPercent}";
                });

                var filePath = await DownloadEpisodeFromGoogleDriveAsync(EpisodeNumber, driveProgress);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var duration = DateTime.Now - startTime;
                    DownloadedFilePath = filePath;
                    StatusMessage = $"Downloaded from Google Drive in {duration.TotalSeconds:F1}s";
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Downloaded episode {EpisodeNumber} from Google Drive in {duration.TotalSeconds:F1}s");
                    _loggingService.Info("Download", $"Downloaded episode {EpisodeNumber} from Google Drive in {duration.TotalSeconds:F1}s");
                    downloadedFromDrive = true;
                    CheckIfCached();
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Error checking Google Drive for episode: {ex.Message}");
            }

            if (!downloadedFromDrive)
            {
                StatusMessage = $"Getting info for episode {EpisodeNumber}...";

                string? audioUrl = null;
                string? title = null;
                string? description = null;
                DateTime? publishDate = null;

                // Check if metadata exists in Google Drive
                try
                {
                    var metadata = await GetEpisodeMetadataFromGoogleDriveAsync(EpisodeNumber);
                    if (metadata != null)
                    {
                        audioUrl = metadata.AudioUrl;
                        title = metadata.Title;
                        description = metadata.Description;
                        publishDate = metadata.PublishDate;
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Found episode {EpisodeNumber} metadata in Google Drive cache");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Error checking Google Drive metadata: {ex.Message}");
                }

                // If no metadata in Google Drive or no audio URL, get from website
                if (string.IsNullOrEmpty(audioUrl))
                {
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Fetching episode {EpisodeNumber} from website");
                    var episode = await _dotnetRocksService.GetEpisodeInfoAsync(EpisodeNumber);

                    title = episode.Title;
                    description = episode.Description;
                    audioUrl = episode.AudioUrl;
                    publishDate = episode.PublishDate;

                    // Save the fetched metadata to Google Drive for future use
                    if (!string.IsNullOrEmpty(audioUrl))
                    {
                        try
                        {
                            await SaveSingleEpisodeMetadataToGoogleDriveAsync(EpisodeNumber, episode);
                            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Saved episode {EpisodeNumber} metadata to Google Drive cache");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Failed to save metadata to Google Drive: {ex.Message}");
                        }
                    }
                }

                // Update UI with episode info
                EpisodeTitle = title ?? string.Empty;
                EpisodeDescription = description ?? string.Empty;
                EpisodeUrl = audioUrl ?? string.Empty;
                EpisodePublishDate = publishDate;

                if (string.IsNullOrEmpty(audioUrl))
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
                IsBusy = false;

                // Cache the URL in DotnetRocksService so it doesn't fetch again
                _dotnetRocksService.CacheEpisodeUrl(EpisodeNumber, audioUrl);

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

                var duration = DateTime.Now - startTime;
                DownloadedFilePath = filePath;
                StatusMessage = $"Downloaded from web in {duration.TotalSeconds:F1}s";
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Downloaded from {audioUrl} in {duration.TotalSeconds:F1}s to: {filePath}");
                _loggingService.Info("Download", $"Downloaded episode {EpisodeNumber} from web in {duration.TotalSeconds:F1}s");

#if WINDOWS
                // On Windows, also save to Google Drive
                DownloadProgress = 0;
                var uploadProgress = new Progress<double>(p =>
                {
                    DownloadProgress = p;
                    StatusMessage = $"Uploading to Google Drive: {DownloadProgressPercent}";
                });

                var uploadStart = DateTime.Now;
                await SaveEpisodeToGoogleDriveAsync(EpisodeNumber, filePath, uploadProgress);
                var uploadDuration = DateTime.Now - uploadStart;
                StatusMessage = $"Downloaded in {duration.TotalSeconds:F1}s, uploaded in {uploadDuration.TotalSeconds:F1}s";
                _loggingService.Info("GoogleDrive", $"Uploaded episode {EpisodeNumber} to Google Drive in {uploadDuration.TotalSeconds:F1}s");
#endif

                CheckIfCached();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Download failed: {ex}");
            StatusMessage = $"Download failed: {ex.Message}";
            _loggingService.Error("Download", $"Failed to download episode {EpisodeNumber}", ex);
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Download Error", ex.Message, "OK");
            }
        }
        finally
        {
            IsBusy = false;
            DownloadProgress = 0;
        }
    }


    // Media Player Methods
    public void SetMediaElement(MediaElement mediaElement)
    {
        System.Diagnostics.Debug.WriteLine($"[SetMediaElement] START - EpisodeNum: {EpisodeNumber}, IsRestoring: {_isRestoringPosition}");

        // Since this is only called once, we don't need to check for existing MediaElement
        _mediaElement = mediaElement;
        System.Diagnostics.Debug.WriteLine($"[SetMediaElement] MediaElement set");

        // Subscribe to events for tracking and auto-play
        _mediaElement.MediaEnded += OnMediaEnded;
        _mediaElement.StateChanged += OnMediaStateChanged;
        _mediaElement.SeekCompleted += OnSeekCompleted;

        // Only load episode if:
        // 1. Episode is cached
        // 2. MediaElement has no source
        // 3. We haven't already loaded this episode
        if (IsEpisodeCached && _mediaElement.Source == null)
        {
            System.Diagnostics.Debug.WriteLine($"[SetMediaElement] Will load episode {EpisodeNumber}");
            _ = LoadEpisode(); // Fire and forget for initial load
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[SetMediaElement] Skip load - Cached: {IsEpisodeCached}, Source: {_mediaElement.Source != null}");
        }

        System.Diagnostics.Debug.WriteLine($"[SetMediaElement] END");
    }

    private async void OnMediaEnded(object? sender, EventArgs e)
    {
        try
        {
            StopPeriodicSaveTimer();

            // Add a small delay to avoid COM exceptions on Windows
            await Task.Delay(100);

            // Ensure we're on the UI thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Set flag to indicate we're auto-advancing
                _isAutoAdvancing = true;

                // Increment to next episode
                EpisodeNumber++;

                // The increment will trigger CheckIfCached which will:
                // - Load the episode if cached with auto-play
                // - Show download button if not cached
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error playing next episode: {ex.Message}";
        }
    }

    private void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Media state changed: {e.PreviousState} -> {e.NewState}");

            switch (e.NewState)
            {
                case MediaElementState.Playing:
                    // Start periodic save timer when playing
                    StartPeriodicSaveTimer();
                    // Request a debounced save when starting
                    RequestDebouncedSave("playback started");
                    break;

                case MediaElementState.Paused:
                    // Only save when paused by user, not during loading
                    if (e.PreviousState == MediaElementState.Playing)
                    {
                        // Request a debounced save when paused from playing
                        RequestDebouncedSave("playback paused");
                    }
                    StopPeriodicSaveTimer();
                    break;

                case MediaElementState.Stopped:
                    // Only save when stopped by user, not during loading/restoration
                    if (e.PreviousState == MediaElementState.Playing || e.PreviousState == MediaElementState.Paused)
                    {
                        // But skip if we're loading an episode, restoring, position is 0, or episode not loaded
                        if (!_isLoadingEpisode && !_isRestoringPosition && _mediaElement?.Position.TotalSeconds > 0)
                        {
                            // Request a debounced save when stopped from playing/paused
                            RequestDebouncedSave("playback stopped");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Skipping save on stop: loading={_isLoadingEpisode}, restoring={_isRestoringPosition}, position={FormatTime(_mediaElement?.Position.TotalSeconds ?? 0)}");
                        }
                    }
                    StopPeriodicSaveTimer();
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Error handling media state change: {ex.Message}");
        }
    }

    private async void OnSeekCompleted(object? sender, EventArgs e)
    {
        try
        {
            // Skip if we're in the middle of restoring position or loading
            if (_isRestoringPosition || _isLoadingEpisode)
            {
                System.Diagnostics.Debug.WriteLine($"[OnSeekCompleted] Skipping - Restoring: {_isRestoringPosition}, Loading: {_isLoadingEpisode}");
                return;
            }

            // Log the immediate position
            if (_mediaElement != null)
            {
                var immediatePos = _mediaElement.Position.TotalSeconds;
                System.Diagnostics.Debug.WriteLine($"[OnSeekCompleted] Initial position: {FormatTime(immediatePos)}");

                // Wait for position to update or timeout after 1 second
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var maxWaitTime = TimeSpan.FromSeconds(1);
                var checkInterval = TimeSpan.FromMilliseconds(50);

                double finalPos = immediatePos;
                while (stopwatch.Elapsed < maxWaitTime)
                {
                    await Task.Delay(checkInterval);
                    var currentPos = _mediaElement.Position.TotalSeconds;

                    // If position changed, we're done waiting
                    if (Math.Abs(currentPos - immediatePos) > 0.1)
                    {
                        finalPos = currentPos;
                        break;
                    }
                    finalPos = currentPos;
                }

                stopwatch.Stop();
                System.Diagnostics.Debug.WriteLine($"[OnSeekCompleted] Final position: {FormatTime(finalPos)}, Wait time: {stopwatch.ElapsedMilliseconds}ms");

                if (!_isLoadingEpisode)
                {
                    RequestDebouncedSave("seek completed");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Error saving state after seek: {ex.Message}");
        }
    }

    private async Task LoadEpisode(bool autoPlay = false)
    {
        System.Diagnostics.Debug.WriteLine($"[LoadEpisode] START - Episode: {EpisodeNumber}, AutoPlay: {autoPlay}, IsRestoring: {_isRestoringPosition}");

        try
        {
            // Check if MediaElement has been set
            if (_mediaElement == null)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadEpisode] MediaElement not initialized yet, aborting");
                return;
            }

            var filePath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[LoadEpisode] Episode {EpisodeNumber} not in cache, aborting");
                StatusMessage = "Episode not found in cache";
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[LoadEpisode] Found cached file: {filePath}");

            // Set flag to indicate we're loading an episode
            _isLoadingEpisode = true;

            // Ensure we're on the UI thread when modifying MediaElement
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // Stop current playback before changing source
                    _mediaElement!.Stop();

                    // Set the source for the MediaElement
                    _mediaElement.Source = MediaSource.FromFile(filePath);

                    // Set metadata for media notification (Android)
                    _mediaElement.MetadataTitle = $"Episode {EpisodeNumber}";
                    _mediaElement.MetadataArtist = ".NET Rocks! Podcast";

                    StatusMessage = $"Episode {EpisodeNumber} loaded in player";
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Successfully loaded episode {EpisodeNumber}");

                    // Check if we should restore position
                    if (_isRestoringPosition && _lastSavedState != null &&
                        _lastSavedState.EpisodeNumber == EpisodeNumber &&
                        _lastSavedState.Position > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Restoring position for episode {EpisodeNumber} to {FormatTime(_lastSavedState.Position)}");

                        // Small delay to ensure source is loaded
                        await Task.Delay(200);

                        // Seek to saved position
                        var position = TimeSpan.FromSeconds(_lastSavedState.Position);
                        await _mediaElement.SeekTo(position);

                        StatusMessage = $"Episode {EpisodeNumber} restored to {FormatTime(_lastSavedState.Position)}";
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Position restored successfully");

                        // Play if it was playing before
                        if (_lastSavedState.IsPlaying)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Auto-playing because state was playing before");
                            _mediaElement.Play();
                        }

                        _isRestoringPosition = false;
                    }
                    // Only auto-play when specifically requested (e.g., after episode ends)
                    else if (autoPlay)
                    {
                        await SavePlaybackStateAsync(true, "new episode auto play");
                        // Small delay to ensure source is loaded
                        await Task.Delay(100);
                        _mediaElement.Play();
                    }

                    // Clear the loading flag
                    _isLoadingEpisode = false;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to load episode: {ex.Message}";
                    _isLoadingEpisode = false;
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load episode: {ex.Message}";
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
                "Download Episode Range",
                "Open Downloads Folder",
                "Clear All Cache",
                "Cache Episode Metadata to Google Drive");

            if (action == "Download Episode Range")
            {
                await DownloadEpisodeRangeAsync();
            }
            else if (action == "Open Downloads Folder")
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
            else if (action == "Cache Episode Metadata to Google Drive")
            {
                await CacheEpisodeMetadataToGoogleDriveAsync();
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

    private async Task PlayEpisodeAsync()
    {
        // First download the episode
        await DownloadEpisodeAsync();

        // Then load it into the player if download succeeded
        if (IsEpisodeCached)
        {
            await LoadEpisode();
        }
    }

    private async Task DownloadAndPlayEpisodeAsync()
    {
        // First download the episode
        await DownloadEpisodeAsync();

        // Then load it into the player with auto-play if download succeeded
        if (IsEpisodeCached)
        {
            await LoadEpisode(true); // Auto-play when auto-advancing
        }
    }

    private async void CheckIfCached()
    {
        System.Diagnostics.Debug.WriteLine($"[CheckIfCached] Episode: {EpisodeNumber}, IsRestoring: {_isRestoringPosition}");

        var cachedPath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
        IsEpisodeCached = !string.IsNullOrEmpty(cachedPath);
        System.Diagnostics.Debug.WriteLine($"[CheckIfCached] Episode {EpisodeNumber} cached: {IsEpisodeCached}");

        if (IsEpisodeCached)
        {
            DownloadedFilePath = cachedPath ?? string.Empty;

            // Load the episode (MediaElement is always set at this point)
            System.Diagnostics.Debug.WriteLine($"[CheckIfCached] Loading episode {EpisodeNumber}");
            await LoadEpisode(_isAutoAdvancing);

            // Reset the flag after use
            _isAutoAdvancing = false;
        }
        else
        {
            // Episode is not cached

            // If auto-advancing and episode is not cached, download and play it
            if (_isAutoAdvancing)
            {
                _isAutoAdvancing = false; // Reset flag before async operation

                // Download and play the episode
                await DownloadAndPlayEpisodeAsync();
            }
            else
            {
                // Clear MediaElement if the cached episode is removed (only if MediaElement is initialized)
                if (_mediaElement != null)
                {
                    _mediaElement.Stop();
                    _mediaElement.Source = null;
                }
            }
        }

        // Update PlayEpisodeCommand availability
        ((Command)PlayEpisodeCommand).ChangeCanExecute();
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
            // On Android, share the folder path
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Episode Downloads Path",
                Text = downloadPath
            });
            StatusMessage = "Shared folder path";
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

    private async Task CacheEpisodeMetadataToGoogleDriveAsync()
    {
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page == null) return;

            // Ask for start episode number
            var startStr = await window.Page.DisplayPromptAsync(
                "Cache Episode Metadata",
                "Enter starting episode number:",
                placeholder: "e.g., 1",
                initialValue: "1",
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(startStr)) return;

            // Ask for end episode number
            var endStr = await window.Page.DisplayPromptAsync(
                "Cache Episode Metadata",
                "Enter ending episode number:",
                placeholder: "e.g., 100",
                initialValue: "100",
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(endStr)) return;

            if (!int.TryParse(startStr, out int startEpisode) || !int.TryParse(endStr, out int endEpisode))
            {
                StatusMessage = "Invalid episode numbers";
                return;
            }

            if (startEpisode > endEpisode)
            {
                StatusMessage = "Start episode must be less than or equal to end episode";
                return;
            }

            // Process the episodes
            await ProcessEpisodeMetadataBatchAsync(startEpisode, endEpisode);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to cache metadata: {ex.Message}";
        }
    }

    private async Task DownloadEpisodeRangeAsync()
    {
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page == null) return;

            // Ask for start episode number
            var startStr = await window.Page.DisplayPromptAsync(
                "Download Episode Range",
                "Enter starting episode number:",
                placeholder: "e.g., 1001",
                initialValue: EpisodeNumber.ToString(),
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(startStr)) return;

            // Ask for end episode number
            var endStr = await window.Page.DisplayPromptAsync(
                "Download Episode Range",
                "Enter ending episode number:",
                placeholder: "e.g., 1010",
                initialValue: (int.Parse(startStr) + 9).ToString(),
                keyboard: Keyboard.Numeric);

            if (string.IsNullOrEmpty(endStr)) return;

            if (!int.TryParse(startStr, out int startEpisode) || !int.TryParse(endStr, out int endEpisode))
            {
                StatusMessage = "Invalid episode numbers";
                return;
            }

            if (startEpisode > endEpisode)
            {
                StatusMessage = "Start episode must be less than or equal to end episode";
                return;
            }

            // Process the downloads
            await ProcessEpisodeDownloadRangeAsync(startEpisode, endEpisode);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to download range: {ex.Message}";
            _loggingService.Error("DownloadRange", "Failed to download episode range", ex);
        }
    }

    private async Task ProcessEpisodeDownloadRangeAsync(int startEpisode, int endEpisode)
    {
        try
        {
            IsBusy = true;

            int totalEpisodes = endEpisode - startEpisode + 1;
            int successCount = 0;
            int failureCount = 0;
            int skippedCount = 0;

            _loggingService.Info("DownloadRange", $"Starting download range: {startEpisode} to {endEpisode} ({totalEpisodes} episodes)");

            for (int episode = startEpisode; episode <= endEpisode; episode++)
            {
                try
                {
                    int currentIndex = episode - startEpisode + 1;
                    StatusMessage = $"Processing episode {episode} ({currentIndex}/{totalEpisodes})...";
                    _loggingService.Info("DownloadRange", $"Processing episode {episode} ({currentIndex}/{totalEpisodes})");

                    // Check if already cached locally
                    var cachedPath = _dotnetRocksService.GetCachedEpisodePath(episode);
                    if (!string.IsNullOrEmpty(cachedPath))
                    {
                        _loggingService.Info("DownloadRange", $"Episode {episode} already cached locally, skipping");
                        skippedCount++;
                        continue;
                    }

                    // Download the episode (without changing current episode)
                    var progress = new Progress<double>(p =>
                    {
                        DownloadProgress = p;
                        StatusMessage = $"Episode {episode} ({currentIndex}/{totalEpisodes}): {DownloadProgressPercent}";
                    });

                    await DownloadSingleEpisodeAsync(episode, progress);

                    successCount++;
                }
                catch (Exception ex)
                {
                    _loggingService.Error("DownloadRange", $"Failed to download episode {episode}", ex);
                    failureCount++;
                }
            }

            var summary = $"Download complete: {successCount} succeeded, {skippedCount} skipped, {failureCount} failed";
            StatusMessage = summary;
            _loggingService.Info("DownloadRange", summary);

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Page != null)
            {
                await window.Page.DisplayAlert("Download Complete", summary, "OK");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Range download failed: {ex.Message}";
            _loggingService.Error("DownloadRange", "Range download failed", ex);
        }
        finally
        {
            IsBusy = false;
            DownloadProgress = 0;
            // Don't call CheckIfCached() here - we're downloading other episodes, not changing the current one
        }
    }

    private async Task DownloadSingleEpisodeAsync(int episodeNumber, IProgress<double> progress)
    {
        // First, check if episode exists in Google Drive
        try
        {
            var driveFilePath = await DownloadEpisodeFromGoogleDriveAsync(episodeNumber, progress);
            if (!string.IsNullOrEmpty(driveFilePath))
            {
                System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Downloaded episode {episodeNumber} from Google Drive");
                _loggingService.Info("DownloadRange", $"Downloaded episode {episodeNumber} from Google Drive");
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Error checking Google Drive for episode: {ex.Message}");
        }

        string? audioUrl = null;
        string? title = null;
        string? description = null;
        DateTime? publishDate = null;

        // Check if metadata exists in Google Drive
        try
        {
            var metadata = await GetEpisodeMetadataFromGoogleDriveAsync(episodeNumber);
            if (metadata != null)
            {
                audioUrl = metadata.AudioUrl;
                title = metadata.Title;
                description = metadata.Description;
                publishDate = metadata.PublishDate;
                System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Found episode {episodeNumber} metadata in Google Drive cache");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Error checking Google Drive metadata: {ex.Message}");
        }

        // If no metadata in Google Drive or no audio URL, get from website
        if (string.IsNullOrEmpty(audioUrl))
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Fetching episode {episodeNumber} from website");
            var episode = await _dotnetRocksService.GetEpisodeInfoAsync(episodeNumber);

            title = episode.Title;
            description = episode.Description;
            audioUrl = episode.AudioUrl;
            publishDate = episode.PublishDate;

            // Save the fetched metadata to Google Drive for future use
            if (!string.IsNullOrEmpty(audioUrl))
            {
                try
                {
                    await SaveSingleEpisodeMetadataToGoogleDriveAsync(episodeNumber, episode);
                    System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Saved episode {episodeNumber} metadata to Google Drive cache");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DownloadSingleEpisode] Failed to save metadata to Google Drive: {ex.Message}");
                }
            }
        }

        if (string.IsNullOrEmpty(audioUrl))
        {
            throw new Exception($"Could not find audio URL for episode {episodeNumber}");
        }

        // Cache the URL in DotnetRocksService so it doesn't fetch again
        _dotnetRocksService.CacheEpisodeUrl(episodeNumber, audioUrl);

        // Download the episode
        var filePath = await _dotnetRocksService.DownloadEpisodeAsync(episodeNumber, progress);

#if WINDOWS
        // On Windows, also save to Google Drive
        await SaveEpisodeToGoogleDriveAsync(episodeNumber, filePath, progress);
#endif
    }

    private async Task ProcessEpisodeMetadataBatchAsync(int startEpisode, int endEpisode)
    {
        try
        {
            IsBusy = true;
            StatusMessage = $"Processing episodes {startEpisode} to {endEpisode}...";

            // Group episodes by 100s
            int groupSize = 100;

            // Calculate which groups we need to process
            int startGroup = ((startEpisode - 1) / groupSize) * groupSize + 1; // 5 -> 1, 101 -> 101, 250 -> 201
            int endGroup = ((endEpisode - 1) / groupSize) * groupSize + 1;

            // Track overall statistics
            int totalEpisodesProcessed = 0;
            List<string> groupsProcessed = new List<string>();

            // Process each group
            for (int currentGroupStart = startGroup; currentGroupStart <= endGroup; currentGroupStart += groupSize)
            {
                int currentGroupEnd = currentGroupStart + groupSize - 1; // 1->100, 101->200, etc.
                var episodeMetadata = new Dictionary<int, Dictionary<string, object>>();

                // Fetch metadata for all episodes in this group range that fall within user's requested range
                int rangeStart = Math.Max(startEpisode, currentGroupStart);
                int rangeEnd = Math.Min(endEpisode, currentGroupEnd);

                for (int episode = rangeStart; episode <= rangeEnd; episode++)
                {
                    try
                    {
                        StatusMessage = $"Fetching metadata for episode {episode} (Group {currentGroupStart}-{currentGroupEnd})...";

                        // Get episode metadata
                        var episodeInfo = await _dotnetRocksService.GetEpisodeInfoAsync(episode);

                        if (episodeInfo != null)
                        {
                            episodeMetadata[episode] = new Dictionary<string, object>
                            {
                                ["episodeNumber"] = episode,
                                ["title"] = episodeInfo.Title ?? string.Empty,
                                ["description"] = episodeInfo.Description ?? string.Empty,
                                ["audioUrl"] = episodeInfo.AudioUrl ?? string.Empty,
                                ["publishDate"] = episodeInfo.PublishDate?.ToString("yyyy-MM-dd") ?? string.Empty
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to process episode {episode}: {ex.Message}");
                    }
                }

                // Save the group (even if partially filled)
                if (episodeMetadata.Count > 0)
                {
                    await SaveMetadataGroupToGoogleDriveAsync(episodeMetadata, currentGroupStart, currentGroupEnd);
                    totalEpisodesProcessed += episodeMetadata.Count;
                    groupsProcessed.Add($"{currentGroupStart:D4}-{currentGroupEnd:D4}");
                }
            }

            // Log final summary
            System.Diagnostics.Debug.WriteLine($"[METADATA SUMMARY] Completed processing:");
            System.Diagnostics.Debug.WriteLine($"  - Episodes requested: {startEpisode} to {endEpisode}");
            System.Diagnostics.Debug.WriteLine($"  - Total episodes processed: {totalEpisodesProcessed}");
            System.Diagnostics.Debug.WriteLine($"  - Groups updated: {string.Join(", ", groupsProcessed)}");

            StatusMessage = $"Successfully cached {totalEpisodesProcessed} episodes across {groupsProcessed.Count} group(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Batch processing failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveMetadataGroupToGoogleDriveAsync(Dictionary<int, Dictionary<string, object>> episodeMetadata, int groupStart, int groupEnd)
    {
        try
        {
            // Initialize Google Drive if needed
            await _googleDriveService.InitializeAsync();

            // Ensure dotnetrocks folder exists
            string folderId = await EnsureDotnetRocksFolderAsync();

            string fileName = $"dotnetrocks_metadata_{groupStart:D4}_{groupEnd:D4}.json";
            Dictionary<int, Dictionary<string, object>> existingMetadata = new Dictionary<int, Dictionary<string, object>>();
            HashSet<int> existingEpisodes = new HashSet<int>();

            // Check if file already exists in the folder
            var query = $"name='{fileName}' and '{folderId}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(100, query);
            var existingFile = files?.FirstOrDefault();

            if (existingFile != null)
            {
                // Download and merge with existing data
                try
                {
                    using var existingStream = await _googleDriveService.DownloadFileAsync(existingFile.Id);
                    using var reader = new StreamReader(existingStream);
                    var existingJson = await reader.ReadToEndAsync();
                    var tempDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(existingJson);

                    if (tempDict != null)
                    {
                        foreach (var kvp in tempDict)
                        {
                            if (int.TryParse(kvp.Key, out int episodeNum))
                            {
                                existingMetadata[episodeNum] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(kvp.Value.GetRawText())!;
                                existingEpisodes.Add(episodeNum);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading existing file: {ex.Message}");
                }
            }

            // Track what's being added/updated
            List<int> newEpisodes = new List<int>();
            List<int> updatedEpisodes = new List<int>();

            // Merge new metadata with existing (new data overwrites old)
            foreach (var kvp in episodeMetadata)
            {
                if (existingEpisodes.Contains(kvp.Key))
                {
                    updatedEpisodes.Add(kvp.Key);
                }
                else
                {
                    newEpisodes.Add(kvp.Key);
                }
                existingMetadata[kvp.Key] = kvp.Value;
            }

            // Log the changes
            if (newEpisodes.Count > 0)
            {
                newEpisodes.Sort();
                System.Diagnostics.Debug.WriteLine($"[{fileName}] Adding new episodes: {string.Join(", ", newEpisodes)}");
                StatusMessage = $"Adding {newEpisodes.Count} new episodes to {fileName}";
            }

            if (updatedEpisodes.Count > 0)
            {
                updatedEpisodes.Sort();
                System.Diagnostics.Debug.WriteLine($"[{fileName}] Updating existing episodes: {string.Join(", ", updatedEpisodes)}");
                StatusMessage = $"Updating {updatedEpisodes.Count} existing episodes in {fileName}";
            }

            // Convert merged metadata to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(existingMetadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            string fileId;
            string action;
            if (existingFile != null)
            {
                // Update existing file
                await _googleDriveService.UpdateFileAsync(existingFile.Id, stream, "application/json");
                fileId = existingFile.Id;
                action = "Updated";
            }
            else
            {
                // Create new file in the folder
                fileId = await _googleDriveService.UploadFileAsync(fileName, stream, "application/json", folderId);
                action = "Created";
            }

            // Update the meta file
            await UpdateMetaFileAsync(groupStart, groupEnd, fileId);

            // Final status with summary
            var totalEpisodes = existingMetadata.Count;
            StatusMessage = $"{action} {fileName}: {totalEpisodes} total episodes ({newEpisodes.Count} new, {updatedEpisodes.Count} updated)";

            // Also log to debug output
            System.Diagnostics.Debug.WriteLine($"[METADATA] {action} {fileName}:");
            System.Diagnostics.Debug.WriteLine($"  - Total episodes in file: {totalEpisodes}");
            System.Diagnostics.Debug.WriteLine($"  - New episodes added: {newEpisodes.Count}");
            System.Diagnostics.Debug.WriteLine($"  - Existing episodes updated: {updatedEpisodes.Count}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save group {groupStart}-{groupEnd}: {ex.Message}", ex);
        }
    }

    private async Task SaveSingleEpisodeMetadataToGoogleDriveAsync(int episodeNumber, PodcastEpisode episode)
    {
        try
        {
            // Create metadata dictionary for this single episode
            var episodeMetadata = new Dictionary<int, Dictionary<string, object>>
            {
                [episodeNumber] = new Dictionary<string, object>
                {
                    ["episodeNumber"] = episodeNumber,
                    ["title"] = episode.Title ?? string.Empty,
                    ["description"] = episode.Description ?? string.Empty,
                    ["audioUrl"] = episode.AudioUrl ?? string.Empty,
                    ["publishDate"] = episode.PublishDate?.ToString("yyyy-MM-dd") ?? string.Empty
                }
            };

            // Calculate which group this episode belongs to
            int groupSize = 100;
            int groupStart = ((episodeNumber - 1) / groupSize) * groupSize + 1;
            int groupEnd = groupStart + groupSize - 1;

            // Reuse existing save method
            await SaveMetadataGroupToGoogleDriveAsync(episodeMetadata, groupStart, groupEnd);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to save single episode metadata: {ex.Message}", ex);
        }
    }

    private async Task<PodcastEpisode?> GetEpisodeMetadataFromGoogleDriveAsync(int episodeNumber)
    {
        try
        {
            // Initialize Google Drive if needed
            await _googleDriveService.InitializeAsync();

            // Calculate which group file this episode would be in
            int groupSize = 100;
            int groupStart = ((episodeNumber - 1) / groupSize) * groupSize + 1;
            int groupEnd = groupStart + groupSize - 1;

            string fileName = $"dotnetrocks_metadata_{groupStart:D4}_{groupEnd:D4}.json";

            // Try to find the metadata file in the dotnetrocks folder
            const string folderName = "dotnetrocks";
            var folderQuery = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var folders = await _googleDriveService.ListFilesAsync(10, folderQuery);
            var folder = folders?.FirstOrDefault();

            if (folder == null)
            {
                return null; // No metadata folder exists
            }

            // Look for the specific metadata file in the folder
            var fileQuery = $"name='{fileName}' and '{folder.Id}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(10, fileQuery);
            var metadataFile = files?.FirstOrDefault();

            if (metadataFile == null)
            {
                return null; // Metadata file doesn't exist
            }

            // Download and parse the metadata file
            using var stream = await _googleDriveService.DownloadFileAsync(metadataFile.Id);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            var allMetadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);

            if (allMetadata != null && allMetadata.TryGetValue(episodeNumber.ToString(), out var episodeElement))
            {
                var episodeData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(episodeElement.GetRawText());

                if (episodeData != null)
                {
                    // Parse the metadata into PodcastEpisode
                    var info = new PodcastEpisode
                    {
                        Number = episodeNumber
                    };

                    if (episodeData.TryGetValue("title", out var titleObj))
                        info.Title = titleObj?.ToString() ?? string.Empty;

                    if (episodeData.TryGetValue("description", out var descObj))
                        info.Description = descObj?.ToString() ?? string.Empty;

                    if (episodeData.TryGetValue("audioUrl", out var urlObj))
                        info.AudioUrl = urlObj?.ToString() ?? string.Empty;

                    if (episodeData.TryGetValue("publishDate", out var dateObj) &&
                        DateTime.TryParse(dateObj?.ToString(), out var date))
                        info.PublishDate = date;

                    return info;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Failed to get metadata from Google Drive: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> DownloadEpisodeFromGoogleDriveAsync(int episodeNumber, IProgress<double>? progress = null)
    {
        try
        {
            // Initialize Google Drive if needed
            await _googleDriveService.InitializeAsync();

            // Check if authenticated
            if (!await _googleDriveService.IsAuthenticatedAsync())
            {
                return null;
            }

            // Look for the episodes folder in dotnetrocks
            const string folderName = "dotnetrocks";
            var folderQuery = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var folders = await _googleDriveService.ListFilesAsync(10, folderQuery);
            var folder = folders?.FirstOrDefault();

            if (folder == null)
            {
                return null;
            }

            // Look for episodes subfolder
            var episodesFolderQuery = $"name='episodes' and mimeType='application/vnd.google-apps.folder' and '{folder.Id}' in parents and trashed=false";
            var episodesFolders = await _googleDriveService.ListFilesAsync(10, episodesFolderQuery);
            var episodesFolder = episodesFolders?.FirstOrDefault();

            if (episodesFolder == null)
            {
                return null;
            }

            // Look for the specific episode file
            var fileName = $"dotnetrocks_{episodeNumber}.mp3";
            var fileQuery = $"name='{fileName}' and '{episodesFolder.Id}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(10, fileQuery);
            var episodeFile = files?.FirstOrDefault();

            if (episodeFile == null)
            {
                return null;
            }

            // Download the file to local cache
            var localFolder = Path.Combine(FileSystem.CacheDirectory, "dotnetrocks", "podcasts");
            Directory.CreateDirectory(localFolder);
            var localPath = Path.Combine(localFolder, fileName);

            System.Diagnostics.Debug.WriteLine($"[DownloadFromGoogleDrive] Downloading episode {episodeNumber} from Google Drive...");
            _loggingService.Info("GoogleDrive", $"Downloading episode {episodeNumber} from Google Drive");

            // Create a progress wrapper to update UI
            var driveProgress = progress != null ? new Progress<double>(p =>
            {
                progress.Report(p);
            }) : null;

            using var stream = await _googleDriveService.DownloadFileAsync(episodeFile.Id, driveProgress);
            using var fileStream = File.Create(localPath);
            await stream.CopyToAsync(fileStream);

            System.Diagnostics.Debug.WriteLine($"[DownloadFromGoogleDrive] Successfully downloaded episode {episodeNumber} to {localPath}");
            _loggingService.Info("GoogleDrive", $"Successfully downloaded episode {episodeNumber} from Google Drive");

            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DownloadFromGoogleDrive] Error downloading episode {episodeNumber}: {ex.Message}");
            return null;
        }
    }

    private async Task SaveEpisodeToGoogleDriveAsync(int episodeNumber, string filePath, IProgress<double>? progress = null)
    {
        try
        {
            // Initialize Google Drive if needed
            await _googleDriveService.InitializeAsync();

            // Check if authenticated
            if (!await _googleDriveService.IsAuthenticatedAsync())
            {
                System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Not authenticated, skipping upload");
                return;
            }

            // Ensure dotnetrocks folder exists
            var folderId = await EnsureDotnetRocksFolderAsync();

            // Ensure episodes subfolder exists
            const string episodesFolderName = "episodes";
            var episodesFolderQuery = $"name='{episodesFolderName}' and mimeType='application/vnd.google-apps.folder' and '{folderId}' in parents and trashed=false";
            var episodesFolders = await _googleDriveService.ListFilesAsync(10, episodesFolderQuery);
            var episodesFolder = episodesFolders?.FirstOrDefault();

            string episodesFolderId;
            if (episodesFolder == null)
            {
                // Create episodes folder
                episodesFolderId = await _googleDriveService.CreateFolderAsync(episodesFolderName, folderId);
                System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Created episodes folder with ID: {episodesFolderId}");
            }
            else
            {
                episodesFolderId = episodesFolder.Id;
            }

            // Check if file already exists
            var fileName = $"dotnetrocks_{episodeNumber}.mp3";
            var fileQuery = $"name='{fileName}' and '{episodesFolderId}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(10, fileQuery);
            var existingFile = files?.FirstOrDefault();

            if (existingFile != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Episode {episodeNumber} already exists in Google Drive, skipping upload");
                return;
            }

            // Upload the file
            System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Uploading episode {episodeNumber} to Google Drive...");
            _loggingService.Info("GoogleDrive", $"Uploading episode {episodeNumber} to Google Drive");

            // Create a progress wrapper to update UI
            var uploadProgress = progress != null ? new Progress<double>(p =>
            {
                progress.Report(p);
            }) : null;

            using var fileStream = File.OpenRead(filePath);
            var fileId = await _googleDriveService.UploadFileAsync(fileName, fileStream, "audio/mpeg", episodesFolderId, uploadProgress);

            System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Successfully uploaded episode {episodeNumber} to Google Drive with ID: {fileId}");
            _loggingService.Info("GoogleDrive", $"Successfully uploaded episode {episodeNumber} to Google Drive");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveToGoogleDrive] Error uploading episode {episodeNumber}: {ex.Message}");
            _loggingService.Error("GoogleDrive", $"Failed to upload episode {episodeNumber} to Google Drive", ex);
        }
    }

    private async Task<string> EnsureDotnetRocksFolderAsync()
    {
        try
        {
            const string folderName = "dotnetrocks";

            // Check if folder already exists
            var query = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
            var folders = await _googleDriveService.ListFilesAsync(10, query);
            var existingFolder = folders?.FirstOrDefault();

            if (existingFolder != null)
            {
                return existingFolder.Id;
            }

            // Create the folder
            var folderId = await _googleDriveService.CreateFolderAsync(folderName);

            System.Diagnostics.Debug.WriteLine($"[METADATA] Created 'dotnetrocks' folder in Google Drive with ID: {folderId}");

            return folderId;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to ensure dotnetrocks folder exists: {ex.Message}", ex);
        }
    }

    private async Task UpdateMetaFileAsync(int groupStart, int groupEnd, string fileId)
    {
        try
        {
            // Ensure dotnetrocks folder exists
            string folderId = await EnsureDotnetRocksFolderAsync();

            const string metaFileName = "dotnetrocks_metadata_index.json";
            Dictionary<string, object>? metaData = null;

            // Try to find existing meta file in the folder
            var query = $"name='{metaFileName}' and '{folderId}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(100, query);
            var metaFile = files?.FirstOrDefault();

            if (metaFile != null)
            {
                // Download and parse existing meta file
                using var stream = await _googleDriveService.DownloadFileAsync(metaFile.Id);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                metaData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }

            // Initialize meta data if needed
            metaData ??= new Dictionary<string, object>
            {
                ["lastUpdated"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["groups"] = new Dictionary<string, object>()
            };

            // Update groups information
            var groups = metaData["groups"] as Dictionary<string, object> ?? new Dictionary<string, object>();
            groups[$"{groupStart:D4}_{groupEnd:D4}"] = new Dictionary<string, object>
            {
                ["fileId"] = fileId,
                ["startEpisode"] = groupStart,
                ["endEpisode"] = groupEnd,
                ["lastUpdated"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            metaData["groups"] = groups;
            metaData["lastUpdated"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            // Save updated meta file
            var updatedJson = System.Text.Json.JsonSerializer.Serialize(metaData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            using var uploadStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedJson));

            if (metaFile != null)
            {
                // Update existing file
                await _googleDriveService.UpdateFileAsync(metaFile.Id, uploadStream, "application/json");
            }
            else
            {
                // Create new file in the folder
                await _googleDriveService.UploadFileAsync(metaFileName, uploadStream, "application/json", folderId);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to update meta file: {ex.Message}", ex);
        }
    }

    // Playback State Management Methods
    public async Task SaveStateOnBackgroundAsync()
    {
        // Public method to save state when app goes to background or navigating away
        await SavePlaybackStateAsync(force: true, reason: "app backgrounded");

        // Don't stop the periodic timer if media is still playing
        // (we want to keep saving state while playing in background)
        if (_mediaElement?.CurrentState != MediaElementState.Playing)
        {
            StopPeriodicSaveTimer();
        }
    }

    public void PauseIfPlaying()
    {
        // Pause playback if currently playing (used when navigating away)
        if (_mediaElement != null && _mediaElement.CurrentState == MediaElementState.Playing)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Pausing playback when navigating away");
            _mediaElement.Pause();
        }
    }

    private async Task LoadPlaybackStateAsync()
    {
        PlaybackState? state = null;
        string? json = null;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] START - Episode: {EpisodeNumber}");

            // Try loading from Google Drive first if we have internet
            var networkAccess = Connectivity.Current.NetworkAccess;
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Network access: {networkAccess}");

            if (networkAccess == NetworkAccess.Internet)
            {
                // Initialize Google Drive first
                await _googleDriveService.InitializeAsync();

                var isAuthenticated = await _googleDriveService.IsAuthenticatedAsync();
                System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Google Drive authenticated: {isAuthenticated}");

                if (isAuthenticated)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Attempting to load from Google Drive");

                // Look for playback state file in dotnetrocks folder
                const string fileName = "dotnetrocks_playback_state.json";
                const string folderName = "dotnetrocks";

                var folderQuery = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
                var folders = await _googleDriveService.ListFilesAsync(10, folderQuery);
                var folder = folders?.FirstOrDefault();

                if (folder != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Found dotnetrocks folder: {folder.Id}");
                    var fileQuery = $"name='{fileName}' and '{folder.Id}' in parents and trashed=false";
                    var files = await _googleDriveService.ListFilesAsync(10, fileQuery);
                    var stateFile = files?.FirstOrDefault();

                    if (stateFile != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Found playback state file in Google Drive, downloading...");
                        using var stream = await _googleDriveService.DownloadFileAsync(stateFile.Id);
                        using var reader = new StreamReader(stream);
                        json = await reader.ReadToEndAsync();
                        System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Successfully downloaded from Google Drive");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] No playback state file found in Google Drive");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] No dotnetrocks folder found in Google Drive");
                }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Skipping Google Drive (not authenticated)");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Skipping Google Drive (offline)");
            }

            // If Google Drive failed or we're offline, try loading from local file
            if (json == null)
            {
                var localPath = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks", "playback_state.json");
                if (File.Exists(localPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Loading from local file");
                    _loggingService.Info("PlaybackState", "Loading from local backup file");
                    json = await File.ReadAllTextAsync(localPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] No playback state found (online or offline)");
                    return;
                }
            }

            // Parse the state file
            state = JsonSerializer.Deserialize<PlaybackState>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (state == null)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Failed to deserialize playback state");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Loaded state: Episode {state.EpisodeNumber}, Position: {state.PositionFormatted}, LastUpdated: {state.LastUpdated}, Device: {state.DeviceName}");

            _lastSavedState = state;

            // Always set the episode number from saved state
            EpisodeNumber = state.EpisodeNumber;
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Set episode to {state.EpisodeNumber}");

            // If position is 0, just load the episode
            if (state.Position <= 0)
            {
                StatusMessage = $"Loaded episode {state.EpisodeNumber}";
                System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Episode {state.EpisodeNumber} at position {state.PositionFormatted}");
                return;
            }

            // Restore position since it's greater than 0
            _isRestoringPosition = true;
            StatusMessage = $"Found previous playback at {FormatTime(state.Position)}";
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Will restore to {FormatTime(state.Position)} when episode loads");

            // If episode is already loaded, restore position now
            if (_mediaElement!.Source != null)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Episode already loaded, restoring position now");
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Task.Delay(200); // Small delay to ensure media is ready
                    var position = TimeSpan.FromSeconds(state.Position);
                    await _mediaElement.SeekTo(position);
                    System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Position restored to {FormatTime(state.Position)}");

                    if (state.IsPlaying)
                    {
                        _mediaElement.Play();
                        System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Resuming playback");
                    }

                    // Don't clear _isRestoringPosition here - let OnSeekCompleted clear it
                });
            }
        }
        catch (Google.GoogleApiException gex) when (gex.Message.Contains("Unauthorized"))
        {
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Google Drive authentication failed: {gex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Failed to load playback state: {ex.Message}");
            _loggingService.Error("PlaybackState", "Failed to load playback state", ex);
        }
        finally
        {
            // Mark initialization as complete
            _isInitializing = false;
            System.Diagnostics.Debug.WriteLine($"[LoadPlaybackState] Initialization complete");
        }
    }

    private void RequestDebouncedSave(string reason)
    {
        // Don't save during initialization
        if (_isInitializing)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] RequestDebouncedSave ({reason}): Skipping during initialization");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] RequestDebouncedSave ({reason}): Resetting debounce timer");

        // Store the reason for this save
        _lastSaveReason = reason;

        // Create timer if it doesn't exist
        _debouncedSaveTimer ??= new System.Threading.Timer(async _ =>
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await SavePlaybackStateAsync(force: true, reason: $"debounced ({_lastSaveReason})");
            });
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        // Reset the timer to fire after the debounce delay
        _debouncedSaveTimer.Change(_saveDebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task SavePlaybackStateAsync(bool force = false, string reason = "periodic")
    {
        try
        {
            // Don't save during initialization
            if (_isInitializing)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] SavePlaybackStateAsync ({reason}): Skipping save during initialization");
                return;
            }


            // Check minimum save interval (unless forced)
            if (!force && (DateTime.Now - _lastSaveTime) < _minimumSaveInterval)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] SavePlaybackStateAsync ({reason}): Too soon since last save, skipping");
                return;
            }

            // Check if episode is cached
            bool isEpisodeCached = !string.IsNullOrEmpty(_dotnetRocksService.GetCachedEpisodePath(EpisodeNumber));

            // If episode is not cached, save with position 0
            double currentPosition = 0;
            double currentDuration = 0;

            if (isEpisodeCached && _mediaElement != null)
            {
                if (!reason.Contains("new episode"))
                {
                    currentPosition = _mediaElement.Position.TotalSeconds;
                }
                currentDuration = _mediaElement.Duration.TotalSeconds;
            }

            var positionFormatted = FormatTime(currentPosition);
            var durationFormatted = FormatTime(currentDuration);
            var currentState = _mediaElement?.CurrentState.ToString() ?? "None";
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Preparing to save ({reason}): Position={positionFormatted}, Duration={durationFormatted}, State={currentState}, Cached={isEpisodeCached}");

            // Skip saving during restoration process
            if (_isRestoringPosition && (reason == "seek completed" || reason == "playback paused"))
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Skipping save ({reason}): Position is being restored");
                return;
            }

            int episodeToSave = EpisodeNumber;

            var state = new PlaybackState
            {
                EpisodeNumber = episodeToSave,
                Position = currentPosition,
                Duration = currentDuration,
                LastUpdated = DateTime.Now,
                IsPlaying = _mediaElement?.CurrentState == MediaElementState.Playing,
                EpisodeTitle = EpisodeTitle,
                DeviceId = GetDeviceId(),
                DeviceName = GetDeviceName()
            };

            // Don't save if nothing has changed significantly
            if (!force && _lastSavedState != null &&
                _lastSavedState.EpisodeNumber == state.EpisodeNumber &&
                Math.Abs(_lastSavedState.Position - state.Position) < 2) // Less than 2 seconds difference
            {
                return;
            }

            // Convert state to JSON
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Always save to local file first
            var localFolder = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks");
            Directory.CreateDirectory(localFolder);
            var localPath = Path.Combine(localFolder, "playback_state.json");
            await File.WriteAllTextAsync(localPath, json);

            // Check internet connectivity
            var networkAccess = Connectivity.Current.NetworkAccess;
            if (networkAccess != NetworkAccess.Internet)
            {
                System.Diagnostics.Debug.WriteLine($"[SavePlaybackState] No internet connection, saved locally only ({reason})");
                _loggingService.Warning("PlaybackState", $"No internet connection, saved locally only ({reason})");
                _hasPendingSave = true;
                StatusMessage = $"Saved locally (offline): Episode {state.EpisodeNumber} at {state.PositionFormatted}";
                return;
            }

            // Check if Google Drive is authenticated
            if (!await _googleDriveService.IsAuthenticatedAsync())
            {
                System.Diagnostics.Debug.WriteLine($"[SavePlaybackState] Google Drive not authenticated, saved locally only ({reason})");
                _loggingService.Warning("PlaybackState", $"Google Drive not authenticated ({reason})");
                _hasPendingSave = true;
                return;
            }

            // Initialize google drive service
            await _googleDriveService.InitializeAsync();

            // Ensure dotnetrocks folder exists
            var folderId = await EnsureDotnetRocksFolderAsync();

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Check if file exists
            const string fileName = "dotnetrocks_playback_state.json";
            var fileQuery = $"name='{fileName}' and '{folderId}' in parents and trashed=false";
            var files = await _googleDriveService.ListFilesAsync(10, fileQuery);
            var existingFile = files?.FirstOrDefault();

            if (existingFile != null)
            {
                // Update existing file
                await _googleDriveService.UpdateFileAsync(existingFile.Id, stream, "application/json");
            }
            else
            {
                // Create new file
                await _googleDriveService.UploadFileAsync(fileName, stream, "application/json", folderId);
            }

            // Successfully saved to Google Drive, clear pending flag
            _hasPendingSave = false;

            _lastSavedState = state;
            _lastSaveTime = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[SavePlaybackState] Saved ({reason}): Episode {state.EpisodeNumber}, Position: {state.PositionFormatted}");

            // Log the save operation
            _loggingService.Info("PlaybackState", $"Saved ({reason}): Episode {state.EpisodeNumber} at {state.PositionFormatted}");

            // Update status bar
            StatusMessage = $"Saved: Episode {state.EpisodeNumber} at {state.PositionFormatted}";
        }
        catch (Google.GoogleApiException gex) when (gex.Message.Contains("Unauthorized"))
        {
            System.Diagnostics.Debug.WriteLine($"[SavePlaybackState] Google Drive authentication failed ({reason}): {gex.Message}");
            _loggingService.Error("PlaybackState", $"Google Drive authentication failed during save ({reason})", gex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SavePlaybackState] Failed to save ({reason}): {ex.Message}");
            _loggingService.Error("PlaybackState", $"Failed to save playback state ({reason})", ex);
        }
    }

    private void StartPeriodicSaveTimer()
    {
        StopPeriodicSaveTimer();

        _periodicSaveTimer = new System.Threading.Timer(async _ =>
        {
            if (_mediaElement?.CurrentState == MediaElementState.Playing)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await SavePlaybackStateAsync();
                });
            }
        }, null, _periodicSaveInterval, _periodicSaveInterval);
    }

    private void StopPeriodicSaveTimer()
    {
        _periodicSaveTimer?.Dispose();
        _periodicSaveTimer = null;
    }

    private string GetDeviceId()
    {
        // Generate a unique device ID based on platform
#if ANDROID
        return Android.Provider.Settings.Secure.GetString(
            Android.App.Application.Context.ContentResolver,
            Android.Provider.Settings.Secure.AndroidId) ?? "android-unknown";
#elif WINDOWS
        return System.Environment.MachineName;
#else
        return "unknown-device";
#endif
    }

    private string GetDeviceName()
    {
#if ANDROID
        return $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}";
#elif WINDOWS
        return System.Environment.MachineName;
#else
        return "Unknown Device";
#endif
    }

    private string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
            : $"{timeSpan.Minutes}:{timeSpan.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}