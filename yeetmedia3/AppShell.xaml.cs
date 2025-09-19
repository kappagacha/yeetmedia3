using Yeetmedia3.Views;

namespace Yeetmedia3;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute(nameof(GoogleDriveView), typeof(GoogleDriveView));
        Routing.RegisterRoute(nameof(JsonEditorView), typeof(JsonEditorView));
    }
}
