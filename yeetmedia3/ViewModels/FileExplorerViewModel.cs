using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yeetmedia3.Models;
using Yeetmedia3.Services;

namespace Yeetmedia3.ViewModels;

public class FileExplorerViewModel : INotifyPropertyChanged
{
    private readonly LoggingService _loggingService;
    private string _currentPath = string.Empty;
    private FileSystemItem? _selectedItem;
    private bool _canNavigateUp;

    public FileExplorerViewModel(LoggingService loggingService)
    {
        _loggingService = loggingService;

        NavigateUpCommand = new Command(NavigateUp, () => CanNavigateUp);

        // Start at CacheDirectory
        _currentPath = FileSystem.CacheDirectory;
        LoadDirectory();
    }

    public ObservableCollection<FileSystemItem> Items { get; } = new();

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            _currentPath = value;
            OnPropertyChanged();
            UpdateCanNavigateUp();
        }
    }

    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public bool CanNavigateUp
    {
        get => _canNavigateUp;
        set
        {
            _canNavigateUp = value;
            OnPropertyChanged();
            ((Command)NavigateUpCommand).ChangeCanExecute();
        }
    }

    public ICommand NavigateUpCommand { get; }

    private void LoadDirectory()
    {
        try
        {
            Items.Clear();

            if (!Directory.Exists(CurrentPath))
            {
                _loggingService.Warning("FileExplorer", $"Directory does not exist: {CurrentPath}");
                return;
            }

            _loggingService.Info("FileExplorer", $"Loading directory: {CurrentPath}");

            var allItems = new List<FileSystemItem>();

            // Load directories
            var directories = Directory.GetDirectories(CurrentPath);
            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                allItems.Add(new FileSystemItem
                {
                    Name = dirInfo.Name,
                    FullPath = dirInfo.FullName,
                    IsDirectory = true,
                    LastModified = dirInfo.LastWriteTime
                });
            }

            // Load files
            var files = Directory.GetFiles(CurrentPath);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                allItems.Add(new FileSystemItem
                {
                    Name = fileInfo.Name,
                    FullPath = fileInfo.FullName,
                    IsDirectory = false,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                });
            }

            // Sort by most recently modified first
            var sortedItems = allItems.OrderByDescending(item => item.LastModified);

            foreach (var item in sortedItems)
            {
                Items.Add(item);
            }

            _loggingService.Info("FileExplorer", $"Loaded {directories.Length} directories and {files.Length} files, sorted by most recent");
        }
        catch (UnauthorizedAccessException ex)
        {
            _loggingService.Error("FileExplorer", $"Access denied to directory: {CurrentPath}", ex);
        }
        catch (Exception ex)
        {
            _loggingService.Error("FileExplorer", $"Failed to load directory: {CurrentPath}", ex);
        }
    }

    public void OnItemSelected(FileSystemItem? item)
    {
        if (item == null) return;

        _loggingService.Info("FileExplorer", $"Item selected: {item.Name} (IsDirectory: {item.IsDirectory})");

        if (item.IsDirectory)
        {
            // Navigate into the directory
            CurrentPath = item.FullPath;
            LoadDirectory();
        }
        else
        {
            // File selected - just update SelectedItem for now
            SelectedItem = item;
        }
    }

    private void NavigateUp()
    {
        try
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                CurrentPath = parent.FullName;
                LoadDirectory();
                _loggingService.Info("FileExplorer", $"Navigated up to: {CurrentPath}");
            }
        }
        catch (Exception ex)
        {
            _loggingService.Error("FileExplorer", "Failed to navigate up", ex);
        }
    }

    private void UpdateCanNavigateUp()
    {
        // Can navigate up if we're not at the root
        try
        {
            var parent = Directory.GetParent(CurrentPath);
            CanNavigateUp = parent != null;
        }
        catch
        {
            CanNavigateUp = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
