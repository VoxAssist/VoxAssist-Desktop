using Avalonia.Controls;
using Avalonia.Interactivity;
using VoxAssist.Desktop.ViewModels;
using System.Windows.Input;
using ReactiveUI;

namespace VoxAssist.Desktop.Views;

public partial class LlmEditDialog : Window
{
    public bool IsDeleted { get; private set; }
    
    private bool _isNew;
    public bool IsNew 
    { 
        get => _isNew; 
        set 
        {
            _isNew = value;
            var btn = this.FindControl<Button>("DeleteButton");
            var spacer = this.FindControl<Panel>("DeleteSpacer");
            if (btn != null) btn.IsVisible = !value;
            if (spacer != null) spacer.IsVisible = value;
        }
    }

    public LlmEditDialog()
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

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<StackPanel>("ConfirmationPanel");
        var mainPanel = this.FindControl<StackPanel>("MainPanel");
        if (panel != null && mainPanel != null)
        {
            mainPanel.IsVisible = false;
            panel.IsVisible = true;
        }
    }

    private void ConfirmDeleteClick(object sender, RoutedEventArgs e)
    {
        IsDeleted = true;
        Close(true);
    }

    private void CancelDeleteClick(object sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<StackPanel>("ConfirmationPanel");
        var mainPanel = this.FindControl<StackPanel>("MainPanel");
        if (panel != null && mainPanel != null)
        {
            mainPanel.IsVisible = true;
            panel.IsVisible = false;
        }
    }
}
