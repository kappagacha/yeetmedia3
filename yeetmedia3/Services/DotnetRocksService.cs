using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Yeetmedia3.Services;

public class DotnetRocksService
{
    private readonly HttpClient _httpClient;
    private readonly WebViewService _webViewService;
    private const string BaseUrl = "https://www.dotnetrocks.com";
    private const string RssFeedUrl = "http://www.pwop.com/feed.aspx?show=dotnetrocks&filetype=master";
    private Dictionary<int, string> _episodeUrlCache = new Dictionary<int, string>();

    public DotnetRocksService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Increase timeout for large downloads
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _webViewService = new WebViewService();
    }

    public DotnetRocksService(WebViewService webViewService)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _webViewService = webViewService;
    }

    private async Task<string> GetCachedRssFeedAsync()
    {
        var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks");
        Directory.CreateDirectory(cacheDir);
        var cacheFile = Path.Combine(cacheDir, "rss_feed.xml");
        var cacheMetaFile = Path.Combine(cacheDir, "rss_feed.meta");

        // Check if cache exists and is still valid (24 hours)
        if (File.Exists(cacheFile) && File.Exists(cacheMetaFile))
        {
            var metaContent = await File.ReadAllTextAsync(cacheMetaFile);
            if (DateTime.TryParse(metaContent, out var cacheTime))
            {
                if (DateTime.Now - cacheTime < TimeSpan.FromHours(24))
                {
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Using cached RSS feed (cached at {cacheTime})");
                    return await File.ReadAllTextAsync(cacheFile);
                }
            }
        }

        // Fetch fresh RSS feed
        System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Fetching fresh RSS feed");
        var rssContent = await _httpClient.GetStringAsync(RssFeedUrl);

        // Cache the feed
        await File.WriteAllTextAsync(cacheFile, rssContent);
        await File.WriteAllTextAsync(cacheMetaFile, DateTime.Now.ToString("O"));

        System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] RSS feed cached to disk");
        return rssContent;
    }

    private async Task<PodcastEpisode?> GetEpisodeFromRssFeed(int episodeNumber)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Looking for episode {episodeNumber} in RSS feed");

            // Get RSS feed (from cache or fresh)
            var rssContent = await GetCachedRssFeedAsync();

            // Parse RSS feed
            var doc = XDocument.Parse(rssContent);
            var ns = doc.Root?.GetDefaultNamespace();
            if (ns == null) return null;

            // Find all episodes
            var items = doc.Descendants(ns + "item");

            foreach (var item in items)
            {
                var title = item.Element(ns + "title")?.Value ?? "";

                // Try to extract episode number from title
                // Common patterns: "Episode 1900", "Show #1900", "1900:", etc.
                var patterns = new[]
                {
                    @"Episode\s+(\d+)",
                    @"Show\s+#?(\d+)",
                    @"^(\d+):",
                    @"#(\d+)\s",
                    @"\s(\d{4})\s"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(title, pattern);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var epNum))
                    {
                        if (epNum == episodeNumber)
                        {
                            // Found the episode, extract all info
                            var episode = new PodcastEpisode
                            {
                                Number = episodeNumber,
                                Title = title,
                                PageUrl = $"{BaseUrl}/details/{episodeNumber}"
                            };

                            // Get description
                            var description = item.Element(ns + "description")?.Value ?? "";
                            if (!string.IsNullOrEmpty(description))
                            {
                                // Remove HTML tags
                                description = Regex.Replace(description, "<.*?>", string.Empty);
                                episode.Description = System.Net.WebUtility.HtmlDecode(description.Trim()) ?? string.Empty;
                            }

                            // Get publish date
                            var pubDate = item.Element(ns + "pubDate")?.Value;
                            if (!string.IsNullOrEmpty(pubDate) && DateTime.TryParse(pubDate, out var date))
                            {
                                episode.PublishDate = date;
                            }

                            // Try to get the enclosure URL from RSS
                            var enclosure = item.Element(ns + "enclosure");
                            if (enclosure != null)
                            {
                                var url = enclosure.Attribute("url")?.Value;
                                if (!string.IsNullOrEmpty(url))
                                {
                                    episode.AudioUrl = url;
                                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Found episode {episodeNumber} with audio URL in RSS: {url}");
                                    _episodeUrlCache[episodeNumber] = url;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Found episode {episodeNumber} metadata in RSS (no audio URL)");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Found episode {episodeNumber} metadata in RSS (no enclosure)");
                            }

                            return episode;
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Episode {episodeNumber} not found in RSS feed");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Error fetching RSS feed: {ex.Message}");
            return null;
        }
    }

    public async Task<PodcastEpisode> GetEpisodeInfoAsync(int episodeNumber)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Getting info for episode {episodeNumber}");

            // Create basic episode info
            var episode = new PodcastEpisode
            {
                Number = episodeNumber,
                PageUrl = $"{BaseUrl}/details/{episodeNumber}",
                Title = $".NET Rocks! Episode {episodeNumber}",
                Description = $"Episode {episodeNumber} of .NET Rocks! podcast"
            };

            // Try to get episode metadata from RSS feed (title, description, date, and possibly audio URL)
            var rssEpisode = await GetEpisodeFromRssFeed(episodeNumber);
            if (rssEpisode != null)
            {
                episode.Title = rssEpisode.Title ?? episode.Title;
                episode.Description = rssEpisode.Description ?? episode.Description;
                episode.PublishDate = rssEpisode.PublishDate;

                // If RSS has audio URL, use it and cache it
                if (!string.IsNullOrEmpty(rssEpisode.AudioUrl))
                {
                    episode.AudioUrl = rssEpisode.AudioUrl;
                    _episodeUrlCache[episodeNumber] = rssEpisode.AudioUrl;
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Using audio URL from RSS feed: {rssEpisode.AudioUrl}");
                }
            }

            // Only use WebView if we don't have audio URL from RSS
            if (string.IsNullOrEmpty(episode.AudioUrl) && _webViewService != null)
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Trying WebView to extract audio URL from page");

                try
                {
                    var pageUrl = $"{BaseUrl}/details/{episodeNumber}";
                    var extractedUrl = await _webViewService.ExtractAudioUrlFromPage(pageUrl);

                    if (!string.IsNullOrEmpty(extractedUrl))
                    {
                        episode.AudioUrl = extractedUrl;
                        _episodeUrlCache[episodeNumber] = extractedUrl;
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Found URL via WebView: {extractedUrl}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] WebView returned null/empty URL");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] WebView extraction failed with exception: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                }
            }

            // Final check - if we still couldn't find a working URL
            if (string.IsNullOrEmpty(episode.AudioUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Warning: Could not find audio URL for episode {episodeNumber}");
                episode.AudioUrl = "";
            }

            return episode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Error getting episode {episodeNumber} info: {ex}");
            throw new Exception($"Failed to get episode {episodeNumber} info: {ex.Message}", ex);
        }
    }

    public async Task<string> DownloadEpisodeAsync(int episodeNumber, IProgress<double>? progress = null)
    {
        try
        {
            // Check if we already have the URL cached
            string? audioUrl = null;
            if (_episodeUrlCache.TryGetValue(episodeNumber, out var cachedUrl))
            {
                audioUrl = cachedUrl;
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Using cached URL for episode {episodeNumber}");
            }
            else
            {
                // Get episode info if not cached
                System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] No cached URL, fetching episode info for {episodeNumber}");
                var episode = await GetEpisodeInfoAsync(episodeNumber);
                audioUrl = episode.AudioUrl;
            }

            if (string.IsNullOrEmpty(audioUrl))
            {
                throw new Exception($"No audio URL found for episode {episodeNumber}");
            }

            // Create filename
            var fileName = $"dotnetrocks_{episodeNumber:D4}.mp3";
            var downloadPath = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks", "podcasts");
            Directory.CreateDirectory(downloadPath);

            var filePath = Path.Combine(downloadPath, fileName);

            // Check if already downloaded
            if (File.Exists(filePath))
            {
                return filePath;
            }

            // Download the file
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Downloading from: {audioUrl}");
            using var response = await _httpClient.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            var totalRead = 0L;

            using var fileStream = File.Create(filePath);
            using var contentStream = await response.Content.ReadAsStreamAsync();

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report((double)totalRead / totalBytes);
                }
            }

            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Error downloading episode {episodeNumber}: {ex}");
            throw new Exception($"Failed to download episode {episodeNumber}: {ex.Message}", ex);
        }
    }


    public string? GetCachedEpisodePath(int episodeNumber)
    {
        var fileName = $"dotnetrocks_{episodeNumber:D4}.mp3";
        var downloadPath = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks", "podcasts");
        var filePath = Path.Combine(downloadPath, fileName);

        return File.Exists(filePath) ? filePath : null;
}

    public void CacheEpisodeUrl(int episodeNumber, string audioUrl)
    {
        if (!string.IsNullOrEmpty(audioUrl))
        {
            _episodeUrlCache[episodeNumber] = audioUrl;
            System.Diagnostics.Debug.WriteLine($"[DotnetRocksService] Cached URL for episode {episodeNumber}");
        }
    }

    public void ClearCache()
    {
        var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "dotnetrocks");

        // Clear podcasts
        var podcastPath = Path.Combine(cacheDir, "podcasts");
        if (Directory.Exists(podcastPath))
        {
            Directory.Delete(podcastPath, true);
        }

        // Clear RSS feed cache
        var rssFile = Path.Combine(cacheDir, "rss_feed.xml");
        var metaFile = Path.Combine(cacheDir, "rss_feed.meta");
        if (File.Exists(rssFile)) File.Delete(rssFile);
        if (File.Exists(metaFile)) File.Delete(metaFile);

        // Clear URL cache
        _episodeUrlCache.Clear();

        System.Diagnostics.Debug.WriteLine("[DotnetRocksService] All caches cleared");
    }
}

public class PodcastEpisode
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public DateTime? PublishDate { get; set; }
}