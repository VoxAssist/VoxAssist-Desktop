using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VoxAssist.Desktop.Views;

public partial class SaveConfirmationDialog : Window
{
    public SaveConfirmationDialog()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            Close(button.CommandParameter?.ToString());
        }
    }
}
