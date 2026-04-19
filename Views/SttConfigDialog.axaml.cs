using Avalonia.Controls;
using Avalonia.Interactivity;
using VoxAssist.Desktop.ViewModels;

namespace VoxAssist.Desktop.Views;

public partial class SttConfigDialog : Window
{
    public MainWindowViewModel MainVm { get; }

    public SttConfigDialog()
    {
        MainVm = new MainWindowViewModel(); // Design-time fallback
        InitializeComponent();
    }

    public SttConfigDialog(MainWindowViewModel mainVm)
    {
        MainVm = mainVm;
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

    private async void AddProviderClick(object sender, RoutedEventArgs e)
    {
        await MainVm.AddProvider(this);
    }

    private async void EditProviderClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(MainVm.SttProviderName))
        {
            await MainVm.EditProvider(MainVm.SttProviderName, this);
        }
    }
}
