using Yeetmedia3.ViewModels;

namespace Yeetmedia3.Views;

public partial class LogView : ContentPage
{
    public LogView(LogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}