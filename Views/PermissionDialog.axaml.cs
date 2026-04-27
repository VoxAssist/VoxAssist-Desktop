using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VoxAssist.Desktop.Views;

public partial class PermissionDialog : Window
{
    public PermissionDialog()
    {
        InitializeComponent();
    }

    private void Fix_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Later_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
