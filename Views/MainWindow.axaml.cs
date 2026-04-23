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

    private void ConversationItem_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is InteractionRecord record && DataContext is MainWindowViewModel vm)
        {
            var props = e.GetCurrentPoint(control).Properties;
            
            if (e.ClickCount == 2 && props.IsLeftButtonPressed)
            {
                _ = vm.ShowDebug(record);
                e.Handled = true;
            }
            else if (props.IsLeftButtonPressed || props.IsRightButtonPressed)
            {
                // Single left or any right click opens menu
                control.ContextMenu?.Open(control);
            }
        }
    }

    private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is TabControl tabControl)
        {
            // We only care about explicit user tab switching (not internal updates)
            if (e.Source is TabControl && e.RemovedItems.Count > 0)
            {
                var oldTab = e.RemovedItems[0] as TabItem;
                if (oldTab?.Header?.ToString() == "Actions" && vm.CanSaveSelectedAction)
                {
                    // Find the index of the old tab
                    int oldIndex = -1;
                    for (int i = 0; i < tabControl.Items.Count; i++)
                    {
                        if (tabControl.Items[i] == oldTab) { oldIndex = i; break; }
                    }

                    if (oldIndex != -1 && tabControl.SelectedIndex != oldIndex)
                    {
                        // Temporarily revert UI selection while we ask the user
                        int newIndex = tabControl.SelectedIndex;
                        tabControl.SelectedIndex = oldIndex;
                        
                        _ = vm.HandleTabChangeWithUnsavedChanges(newIndex);
                    }
                }
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
