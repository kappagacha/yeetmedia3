using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class GoogleDriveView : ContentPage
{
    public GoogleDriveView(GoogleDriveViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}