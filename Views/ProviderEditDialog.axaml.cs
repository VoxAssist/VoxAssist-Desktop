using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VoxAssist.Desktop.Views;

public partial class ProviderEditDialog : Window
{
    public ProviderEditDialog()
    {
        InitializeComponent();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
