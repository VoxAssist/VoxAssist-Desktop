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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
