using Avalonia.Controls;
using Avalonia.Interactivity;
using VoxAssist.Desktop.ViewModels;

namespace VoxAssist.Desktop.Views;

public partial class LlmEditDialog : Window
{
    public bool IsDeleted { get; private set; }
    public bool IsNew { get; set; }
    public MainWindowViewModel MainVm { get; }

    public LlmEditDialog()
    {
        InitializeComponent();
        MainVm = new MainWindowViewModel(); // Design-time fallback
    }

    public LlmEditDialog(MainWindowViewModel mainVm)
    {
        InitializeComponent();
        MainVm = mainVm;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        MainPanel.IsVisible = false;
        ConfirmationPanel.IsVisible = true;
    }

    private void ConfirmDeleteClick(object sender, RoutedEventArgs e)
    {
        IsDeleted = true;
        Close(true);
    }

    private void CancelDeleteClick(object sender, RoutedEventArgs e)
    {
        ConfirmationPanel.IsVisible = false;
        MainPanel.IsVisible = true;
    }

    private async void AddProviderClick(object sender, RoutedEventArgs e)
    {
        await MainVm.AddProvider(this);
    }
}
