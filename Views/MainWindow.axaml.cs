using Avalonia.Controls;
using VoxAssist.Desktop.ViewModels;
using System.Collections.Generic;
using SharpHook.Native;
using System;

namespace VoxAssist.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ConversationListBox_TemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Conversation.CollectionChanged += (s, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var listBox = this.FindControl<ListBox>("ConversationListBox");
                        if (listBox != null)
                        {
                            // Use ScrollIntoView to ensure the last item is visible
                            if (vm.Conversation.Count > 0)
                            {
                                listBox.ScrollIntoView(vm.Conversation[vm.Conversation.Count - 1]);
                            }
                        }
                    });
                }
            };
        }
    }

    private async void LlmDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.EditSelectedLlm();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
