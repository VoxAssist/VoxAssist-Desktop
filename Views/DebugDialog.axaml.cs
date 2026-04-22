using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace VoxAssist.Desktop.Views;

public partial class DebugDialog : Window
{
    public DebugDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }
}
