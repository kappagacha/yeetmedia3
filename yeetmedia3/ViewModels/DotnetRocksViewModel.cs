using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
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

    // Media player properties
    private MediaElement? _mediaElement;
    private bool _isAutoAdvancing = false;

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
        ShowActionsCommand = new Command(async () => await ShowActionsAsync());
        IncrementEpisodeCommand = new Command(() => EpisodeNumber++);
        DecrementEpisodeCommand = new Command(() => { if (EpisodeNumber > 1) EpisodeNumber--; });
        PlayEpisodeCommand = new Command(async () => await PlayEpisodeAsync(), () => !IsDownloading && !IsEpisodeCached);

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
        }
    }

    public ICommand DownloadEpisodeCommand { get; }
    public ICommand ShowActionsCommand { get; }
    public ICommand IncrementEpisodeCommand { get; }
    public ICommand DecrementEpisodeCommand { get; }
    public ICommand PlayEpisodeCommand { get; }


    private async Task DownloadEpisodeAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksViewModel] Starting download for episode {EpisodeNumber}");
            IsDownloading = true;
            IsLoading = true;
            DownloadProgress = 0;
            StatusMessage = $"Getting info for episode {EpisodeNumber}...";

            string? audioUrl = null;
            string? title = null;
            string? description = null;
            DateTime? publishDate = null;

            // First, check if metadata exists in Google Drive
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
            IsLoading = false;

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


    // Media Player Methods
    public void SetMediaElement(MediaElement mediaElement)
    {
        _mediaElement = mediaElement;

        // Subscribe to MediaEnded event to auto-play next episode
        _mediaElement.MediaEnded += OnMediaEnded;

        // If episode is already cached when MediaElement is set, load it
        if (IsEpisodeCached)
        {
            LoadEpisode();
        }
    }

    private async void OnMediaEnded(object? sender, EventArgs e)
    {
        try
        {
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

    private void LoadEpisode(bool autoPlay = false)
    {
        try
        {
            if (_mediaElement == null)
            {
                StatusMessage = "Media player not initialized";
                return;
            }

            var filePath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "Episode not found in cache";
                return;
            }

            // Ensure we're on the UI thread when modifying MediaElement
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Stop current playback before changing source
                    _mediaElement.Stop();

                    // Set the source for the MediaElement
                    _mediaElement.Source = MediaSource.FromFile(filePath);
                    StatusMessage = $"Episode {EpisodeNumber} loaded in player";

                    // Only auto-play when specifically requested (e.g., after episode ends)
                    if (autoPlay)
                    {
                        // Small delay to ensure source is loaded
                        await Task.Delay(100);
                        _mediaElement.Play();
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to load episode: {ex.Message}";
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
                "Open Downloads Folder",
                "Clear All Cache",
                "Cache Episode Metadata to Google Drive");

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
            LoadEpisode();
        }
    }

    private async Task DownloadAndPlayEpisodeAsync()
    {
        // First download the episode
        await DownloadEpisodeAsync();

        // Then load it into the player with auto-play if download succeeded
        if (IsEpisodeCached)
        {
            LoadEpisode(true); // Auto-play when auto-advancing
        }
    }

    private async void CheckIfCached()
    {
        var cachedPath = _dotnetRocksService.GetCachedEpisodePath(EpisodeNumber);
        IsEpisodeCached = !string.IsNullOrEmpty(cachedPath);

        if (IsEpisodeCached)
        {
            DownloadedFilePath = cachedPath ?? string.Empty;
            // Automatically load the cached episode into the media player
            // Pass true for autoPlay only if we're auto-advancing from a finished episode
            LoadEpisode(_isAutoAdvancing);

            // Reset the flag after use
            _isAutoAdvancing = false;
        }
        else
        {
            // If auto-advancing and episode is not cached, download and play it
            if (_isAutoAdvancing)
            {
                _isAutoAdvancing = false; // Reset flag before async operation

                // Download and play the episode
                await DownloadAndPlayEpisodeAsync();
            }
            else
            {
                // Clear MediaElement if the cached episode is removed
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
            // On Android, share the folder path for file managers to open
            try
            {
                // Share the folder path as text that file managers can recognize
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Title = "Open Downloads Folder",
                    Text = downloadPath,
                    Subject = "Episode Downloads Location"
                });
                StatusMessage = "Shared folder path";
            }
            catch
            {
                // If sharing doesn't work, copy path to clipboard and show message
                try
                {
                    await Clipboard.Default.SetTextAsync(downloadPath);
                    var window = Application.Current?.Windows.FirstOrDefault();
                    if (window?.Page != null)
                    {
                        await window.Page.DisplayAlert("Path Copied",
                            $"Downloads folder path copied to clipboard:\n\n{downloadPath}\n\nPaste this in your file manager to navigate to the folder.", "OK");
                    }
                    StatusMessage = "Path copied to clipboard";
                }
                catch
                {
                    // Final fallback - just show the path
                    var window = Application.Current?.Windows.FirstOrDefault();
                    if (window?.Page != null)
                    {
                        await window.Page.DisplayAlert("Downloads Location",
                            $"Episodes are saved to:\n{downloadPath}", "OK");
                    }
                    StatusMessage = "Downloads path shown";
                }
            }
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

    private async Task ProcessEpisodeMetadataBatchAsync(int startEpisode, int endEpisode)
    {
        try
        {
            IsLoading = true;
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
            IsLoading = false;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}