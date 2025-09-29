using System.Text.Json;
using System.Text.Json.Serialization;
#if WINDOWS
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using System.Threading;
using System.IO;
#endif

namespace Yeetmedia3.Services;

public class GoogleAuthService
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _redirectUri;
    private readonly string[] _scopes;

    public GoogleAuthService(string clientId, string? redirectUri = null, params string[]? scopes)
    {
        _httpClient = new HttpClient();
        _clientId = clientId;
        _redirectUri = redirectUri ?? GetPlatformRedirectUri();
        _scopes = scopes ?? new[] { "https://www.googleapis.com/auth/drive.readonly" };
    }

    private string GetPlatformRedirectUri()
    {
#if ANDROID
        // For Android, use the app's package name as the redirect URI scheme
        return "com.companyname.yeetmedia3://";
#elif WINDOWS
        // For Windows, use a local redirect
        return "http://localhost/oauth2redirect";
#else
        return "http://localhost/oauth2redirect";
#endif
    }

    public async Task<GoogleAuthToken> AuthenticateAsync()
    {
#if WINDOWS
        return await AuthenticateWindowsAsync();
#else
        try
        {
            // Generate random state for security
            var state = Guid.NewGuid().ToString("N");

            // Build the authorization URL
            var authUrl = BuildAuthorizationUrl(state);

            // Use MAUI's WebAuthenticator for the OAuth flow
            var authResult = await WebAuthenticator.Default.AuthenticateAsync(
                new Uri(authUrl),
                new Uri(_redirectUri));

            // Extract the authorization code from the result
            if (authResult?.Properties?.ContainsKey("code") == true)
            {
                var code = authResult.Properties["code"];

                // Exchange the authorization code for tokens
                return await ExchangeCodeForTokenAsync(code);
            }

            throw new Exception("No authorization code received");
        }
        catch (TaskCanceledException)
        {
            throw new Exception("Authentication was cancelled by the user");
        }
        catch (Exception ex)
        {
            throw new Exception($"Authentication failed: {ex.Message}", ex);
        }
#endif
    }

#if WINDOWS
    private async Task<GoogleAuthToken> AuthenticateWindowsAsync()
    {
        try
        {
            // Windows desktop app uses OAuth 2.0 for installed applications (no secret required)
            var clientSecrets = new ClientSecrets
            {
                ClientId = _clientId
                // No client secret needed for desktop installed applications
            };

            var dataStore = new FileDataStore(Path.Combine(FileSystem.AppDataDirectory, "DriveAPI.Auth.Store"));

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                _scopes,
                "user",
                CancellationToken.None,
                dataStore);

            // Get the access token from the credential
            var token = await credential.GetAccessTokenForRequestAsync();

            // Create GoogleAuthToken to match our interface
            var authToken = new GoogleAuthToken
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresAt = DateTime.UtcNow.AddHours(1), // Google tokens typically expire in 1 hour
                Scope = string.Join(" ", _scopes)
            };

            // Save token for consistency
            await SaveTokenAsync(authToken);

            return authToken;
        }
        catch (Exception ex)
        {
            throw new Exception($"Windows authentication failed: {ex.Message}", ex);
        }
    }
#endif

    private string BuildAuthorizationUrl(string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(" ", _scopes),
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };

        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthorizationEndpoint}?{queryString}";
    }

    private async Task<GoogleAuthToken> ExchangeCodeForTokenAsync(string code)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code"] = code,
            ["redirect_uri"] = _redirectUri,
            ["grant_type"] = "authorization_code"
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(TokenEndpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token exchange failed: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<GoogleAuthToken>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (token == null)
        {
            throw new Exception("Failed to deserialize token response");
        }

        // Save the token
        await SaveTokenAsync(token);

        return token;
    }

    public async Task<GoogleAuthToken> RefreshTokenAsync(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(TokenEndpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token refresh failed: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<GoogleAuthToken>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (token == null)
        {
            throw new Exception("Failed to deserialize refresh token response");
        }

        // Update the saved token
        await SaveTokenAsync(token);

        return token;
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetSavedTokenAsync();
        return token != null && !string.IsNullOrEmpty(token.AccessToken);
    }

    public async Task<GoogleAuthToken?> GetValidTokenAsync()
    {
        var token = await GetSavedTokenAsync();

        if (token == null)
        {
            return null;
        }

        // Check if token is expired (with 5 minute buffer)
        if (token.ExpiresAt < DateTime.UtcNow.AddMinutes(-5)) 
        {
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                // Refresh the token
                return await RefreshTokenAsync(token.RefreshToken);
            }
            return null;
        }

        return token;
    }

    private async Task SaveTokenAsync(GoogleAuthToken token)
    {
        if (token == null) return;

        // Calculate expiration time
        if (token.ExpiresIn > 0)
        {
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn);
        }

        var json = JsonSerializer.Serialize(token);
        await SecureStorage.Default.SetAsync("google_auth_token", json);
    }

    private async Task<GoogleAuthToken?> GetSavedTokenAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync("google_auth_token");
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<GoogleAuthToken>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task SignOutAsync()
    {
#if WINDOWS
        // Clear Windows credential store
        var tokenPath = Path.Combine(FileSystem.AppDataDirectory, "DriveAPI.Auth.Store");
        if (Directory.Exists(tokenPath))
        {
            try
            {
                Directory.Delete(tokenPath, true);
            }
            catch
            {
                // Ignore errors when deleting
            }
        }

        // Also clear SecureStorage in case there's any cached data
        try
        {
            SecureStorage.Default.Remove("google_auth_token");
        }
        catch
        {
            // Ignore errors
        }
#else
        var token = await GetSavedTokenAsync();

        if (token != null && !string.IsNullOrEmpty(token.AccessToken))
        {
            try
            {
                // Revoke the token with Google
                var url = $"{RevokeEndpoint}?token={token.AccessToken}";
                await _httpClient.PostAsync(url, null);
            }
            catch
            {
                // Ignore revocation errors
            }
        }

        // Clear the saved token
        SecureStorage.Default.Remove("google_auth_token");
#endif

        // Clear any WebAuthenticator cached sessions (for all platforms)
        try
        {
            await WebAuthenticator.Default.AuthenticateAsync(
                new WebAuthenticatorOptions
                {
                    Url = new Uri($"{AuthorizationEndpoint}?prompt=none"),
                    CallbackUrl = new Uri(_redirectUri)
                });
        }
        catch
        {
            // This will fail, but it clears the session
        }

        // Clear all cached data in app data directory
        try
        {
            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "GoogleAuth");
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }
        catch
        {
            // Ignore errors when deleting cache
        }
    }
}

public class GoogleAuthToken
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}