namespace Yeetmedia3.Services;

public class WebViewService
{
    private WebView? _webView;
    private TaskCompletionSource<string?>? _audioUrlTaskSource;

    public async Task<string?> ExtractAudioUrlFromPage(string pageUrl)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[WebViewService] Loading page: {pageUrl}");

            // This needs to run on the UI thread
            return await MainThread.InvokeOnMainThreadAsync<string?>(async () =>
            {
                _audioUrlTaskSource = new TaskCompletionSource<string?>();

                // Create a temporary invisible page with WebView
                var tempPage = new ContentPage
                {
                    Title = "Loading...",
                    IsVisible = false
                };

                _webView = new WebView
                {
                    Source = pageUrl,
                    IsVisible = true,
                    HeightRequest = 1,
                    WidthRequest = 1,
                    Opacity = 0.01
                };

                tempPage.Content = _webView;

                bool pageLoaded = false;
                _webView.Navigating += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebViewService] Navigating to: {e.Url}");
                };

                _webView.Navigated += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[WebViewService] Navigated: {e.Result}");
                    if (!pageLoaded && e.Result == WebNavigationResult.Success)
                    {
                        pageLoaded = true;

                        // Wait for page to fully load and render
                        await Task.Delay(5000);

                        // Extract audio URL
                        await ExtractAudioUrl();
                    }
                };

                try
                {
                    // Push as modal
                    var currentWindow = Application.Current?.Windows.FirstOrDefault();
                    if (currentWindow?.Page == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[WebViewService] No active window or page found");
                        return null;
                    }
                    await currentWindow.Page.Navigation.PushModalAsync(tempPage, false);

                    // Wait for extraction with timeout
                    var timeoutTask = Task.Delay(45000);
                    var completedTask = await Task.WhenAny(_audioUrlTaskSource!.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        System.Diagnostics.Debug.WriteLine("[WebViewService] Timeout - no audio URL found");
                        _audioUrlTaskSource!.TrySetResult(null);
                    }

                    var result = await _audioUrlTaskSource!.Task;

                    // Clean up
                    await currentWindow.Page.Navigation.PopModalAsync(false);

                    if (!string.IsNullOrEmpty(result))
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebViewService] Found URL: {result}");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebViewService] Error: {ex.Message}");

                    // Try to clean up
                    try
                    {
                        var window = Application.Current?.Windows.FirstOrDefault();
                        if (window?.Page != null)
                        {
                            await window.Page.Navigation.PopModalAsync(false);
                        }
                    }
                    catch { }

                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebViewService] ExtractAudioUrlFromPage error: {ex.Message}");
            return null;
        }
    }

    private async Task ExtractAudioUrl()
    {
        try
        {
            const int maxRetries = 3;
            const int delayMs = 2000;

            for (int retries = 0; retries < maxRetries; retries++)
            {
                try
                {
                    // Get the HTML
                    var html = await _webView!.EvaluateJavaScriptAsync("document.documentElement.outerHTML");

                    if (!string.IsNullOrEmpty(html) && html != "null")
                    {
                        // Unescape the HTML
                        var unescapedHtml = System.Text.RegularExpressions.Regex.Unescape(html);

                        // Look for MP3 URL in source tag
                        if (unescapedHtml.Contains(".mp3"))
                        {
                            // Extract URL from source tag
                            var sourcePattern = @"<source\s+[^>]*src=[""']([^""']+\.mp3[^""']*)[""']";
                            var sourceMatch = System.Text.RegularExpressions.Regex.Match(unescapedHtml, sourcePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            if (sourceMatch.Success)
                            {
                                var mp3Url = sourceMatch.Groups[1].Value;
                                System.Diagnostics.Debug.WriteLine($"[WebViewService] Found MP3 URL: {mp3Url}");

                                if (mp3Url.StartsWith("//"))
                                {
                                    mp3Url = "https:" + mp3Url;
                                }

                                _audioUrlTaskSource!.TrySetResult(mp3Url);
                                return;
                            }
                        }
                    }

                    if (retries < maxRetries - 1)
                    {
                        await Task.Delay(delayMs);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebViewService] Attempt {retries + 1} error: {ex.Message}");
                    if (retries < maxRetries - 1)
                    {
                        await Task.Delay(delayMs);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("[WebViewService] No audio URL found after retries");
            _audioUrlTaskSource?.TrySetResult(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebViewService] ExtractAudioUrl error: {ex.Message}");
            _audioUrlTaskSource?.TrySetResult(null);
        }
    }
}