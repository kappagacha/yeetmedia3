using Yeetmedia3.Models;
using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class FileExplorerView : ContentPage
{
    private readonly FileExplorerViewModel _viewModel;

    public FileExplorerView(FileExplorerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is FileSystemItem item)
        {
            _viewModel.OnItemSelected(item);

            // Clear selection to allow selecting the same item again
            ((CollectionView)sender).SelectedItem = null;
        }
    }
}
