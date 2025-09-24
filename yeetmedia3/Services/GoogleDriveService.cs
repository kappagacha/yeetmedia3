using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Yeetmedia3.Models;
using System.Text.Json;

namespace Yeetmedia3.Services;
    public class GoogleDriveService
    {
        private static readonly string[] Scopes = { DriveService.Scope.Drive };
        private readonly AppSettings _appSettings;
        private DriveService _driveService;
        private GoogleAuthService _authService;

        public GoogleDriveService()
        {
            _appSettings = LoadAppSettings();
        }

        private AppSettings LoadAppSettings()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Yeetmedia3.appsettings.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource {resourceName} not found.");
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        public async Task InitializeAsync()
        {
#if ANDROID
            await InitializeAndroidAsync();
#elif WINDOWS
            await InitializeWindowsAsync();
#else
            throw new PlatformNotSupportedException("Google Drive authentication is not supported on this platform");
#endif
        }

#if ANDROID
        private async Task InitializeAndroidAsync()
        {
            // Use GoogleAuthService for Android
            _authService = new GoogleAuthService(
                _appSettings.GoogleDrive.AndroidClientId,
                null,
                Scopes);

            var token = await _authService.GetValidTokenAsync();

            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                token = await _authService.AuthenticateAsync();
            }

            // Create credential from token
            var credential = GoogleCredential.FromAccessToken(token.AccessToken);

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _appSettings.GoogleDrive.ApplicationName,
            });
        }
#endif

#if WINDOWS
        private async Task InitializeWindowsAsync()
        {
            // Use GoogleAuthService for Windows (unified approach)
            _authService = new GoogleAuthService(
                _appSettings.GoogleDrive.WindowsClientId,
                null,
                Scopes);

            var token = await _authService.GetValidTokenAsync();

            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                token = await _authService.AuthenticateAsync();
            }

            // Create credential from token
            var credential = GoogleCredential.FromAccessToken(token.AccessToken);

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _appSettings.GoogleDrive.ApplicationName,
            });
        }
#endif

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(int pageSize = 10, string query = null)
        {
            if (_driveService == null)
            {
                await InitializeAsync();
            }

            var listRequest = _driveService.Files.List();
            listRequest.PageSize = pageSize;
            listRequest.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, parents, webViewLink)";

            if (!string.IsNullOrEmpty(query))
            {
                listRequest.Q = query;
            }

            var files = await listRequest.ExecuteAsync();
            return files.Files;
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFoldersAsync()
        {
            return await ListFilesAsync(query: "mimeType='application/vnd.google-apps.folder'");
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesInFolderAsync(string folderId)
        {
            return await ListFilesAsync(query: $"'{folderId}' in parents");
        }

        public async Task<Google.Apis.Drive.v3.Data.File> GetFileMetadataAsync(string fileId)
        {
            if (_driveService == null)
            {
                await InitializeAsync();
            }

            var request = _driveService.Files.Get(fileId);
            request.Fields = "id, name, mimeType, size, modifiedTime, parents, webViewLink, description";

            return await request.ExecuteAsync();
        }

        public async Task<Stream> DownloadFileAsync(string fileId)
        {
            if (_driveService == null)
            {
                await InitializeAsync();
            }

            var request = _driveService.Files.Get(fileId);
            var stream = new MemoryStream();

            request.MediaDownloader.ProgressChanged += (progress) =>
            {
                switch (progress.Status)
                {
                    case Google.Apis.Download.DownloadStatus.Downloading:
                        Console.WriteLine($"Downloaded {progress.BytesDownloaded} bytes");
                        break;
                    case Google.Apis.Download.DownloadStatus.Completed:
                        Console.WriteLine("Download complete.");
                        break;
                    case Google.Apis.Download.DownloadStatus.Failed:
                        Console.WriteLine("Download failed.");
                        break;
                }
            };

            await request.DownloadAsync(stream);
            stream.Position = 0;
            return stream;
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> SearchFilesAsync(string searchTerm)
        {
            var query = $"name contains '{searchTerm}'";
            return await ListFilesAsync(pageSize: 50, query: query);
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {
                // Use unified GoogleAuthService for both platforms
                if (_authService != null)
                {
                    return await _authService.IsAuthenticatedAsync();
                }

                if (_driveService == null)
                {
                    return false;
                }

                // Try to list one file to check if authenticated
                var listRequest = _driveService.Files.List();
                listRequest.PageSize = 1;
                await listRequest.ExecuteAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> UploadFileAsync(string fileName, Stream fileContent, string mimeType, string parentFolderId = null)
        {
            if (_driveService == null)
            {
                await InitializeAsync();
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = fileName,
                MimeType = mimeType
            };

            if (!string.IsNullOrEmpty(parentFolderId))
            {
                fileMetadata.Parents = new List<string> { parentFolderId };
            }
            else
            {
                fileMetadata.Parents = new List<string> { "root" };
            }

            FilesResource.CreateMediaUpload request;
            request = _driveService.Files.Create(fileMetadata, fileContent, mimeType);
            request.Fields = "id, name, mimeType, size, modifiedTime, parents, webViewLink";

            var result = await request.UploadAsync();
            if (result.Status == Google.Apis.Upload.UploadStatus.Failed)
            {
                throw new Exception($"Upload failed: {result.Exception?.Message}");
            }

            return request.ResponseBody?.Id;
        }

        public async Task UpdateFileAsync(string fileId, Stream fileContent, string mimeType)
        {
            if (_driveService == null)
            {
                await InitializeAsync();
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File();

            FilesResource.UpdateMediaUpload request;
            request = _driveService.Files.Update(fileMetadata, fileId, fileContent, mimeType);
            request.Fields = "id, name, mimeType, size, modifiedTime, parents, webViewLink";

            var result = await request.UploadAsync();
            if (result.Status == Google.Apis.Upload.UploadStatus.Failed)
            {
                throw new Exception($"Update failed: {result.Exception?.Message}");
            }
        }

        public async Task SignOutAsync()
        {
            // Use unified GoogleAuthService for both platforms
            if (_authService != null)
            {
                await _authService.SignOutAsync();
            }

            _driveService = null;
        }

        public string GetClientId()
        {
#if ANDROID
            return _appSettings.GoogleDrive.AndroidClientId;
#elif WINDOWS
            return _appSettings.GoogleDrive.WindowsClientId;
#else
            return string.Empty;
#endif
        }
    }