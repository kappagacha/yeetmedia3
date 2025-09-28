using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class DotnetRocksView : ContentPage
{
    public DotnetRocksView(DotnetRocksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}