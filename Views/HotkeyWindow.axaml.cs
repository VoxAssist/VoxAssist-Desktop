using Avalonia.Controls;
using Avalonia.Input;
using System.Collections.Generic;
using VoxAssist.Desktop.Services;
using SharpHook.Data;
using System;
using Avalonia.Threading;

namespace VoxAssist.Desktop.Views;

public partial class HotkeyWindow : Window
{
    private readonly HotkeyService? _hotkeyService;
    public List<KeyCode>? RecordedSequence { get; private set; }

    public HotkeyWindow()
    {
        InitializeComponent();
    }

    public HotkeyWindow(HotkeyService hotkeyService, string mode)
    {
        InitializeComponent();
        _hotkeyService = hotkeyService;
        
        var prompt = this.FindControl<TextBlock>("PromptText");
        if (prompt != null) prompt.Text = $"Press Hot Key for {mode} action";
        
        _hotkeyService.HotKeyRecorded += OnHotKeyRecorded;
        
        // Use a small delay to ensure the window is ready
        Dispatcher.UIThread.Post(() => {
            if (_hotkeyService != null) _hotkeyService.IsRecordingMode = true;
            this.Focus();
        }, DispatcherPriority.Background);
    }

    private void OnHotKeyRecorded(List<KeyCode> sequence)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RecordedSequence = sequence;
            Close(sequence);
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_hotkeyService != null)
        {
            _hotkeyService.HotKeyRecorded -= OnHotKeyRecorded;
            _hotkeyService.IsRecordingMode = false;
        }
        base.OnClosing(e);
    }
    
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RecordedSequence = null;
            Close(null);
        }
        base.OnKeyDown(e);
    }
}
