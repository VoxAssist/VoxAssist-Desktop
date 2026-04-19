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
        MainVm = new MainWindowViewModel(); // Design-time fallback
        InitializeComponent();
    }

    public LlmEditDialog(MainWindowViewModel mainVm)
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

    private async void EditProviderClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LlmViewModel model && !string.IsNullOrEmpty(model.ProviderName))
        {
            await MainVm.EditProvider(model.ProviderName, this);
        }
    }
}
