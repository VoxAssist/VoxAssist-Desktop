using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using VoxAssist.Desktop.Views;
using Avalonia.Controls;
using VoxAssist.Desktop.Services;
using Avalonia.Threading;

namespace VoxAssist.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isRespeakerConnected;
    public bool IsRespeakerConnected
    {
        get => _isRespeakerConnected;
        set => this.RaiseAndSetIfChanged(ref _isRespeakerConnected, value);
    }

    private int _doaAngle;
    public int DoaAngle
    {
        get => _doaAngle;
        set => this.RaiseAndSetIfChanged(ref _doaAngle, value);
    }

    private bool _isFreezeEnabled;
    public bool IsFreezeEnabled
    {
        get => _isFreezeEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _isFreezeEnabled, value);
            if (_isInitialized) _respeaker.Write(19, 6, value ? 1 : 0);
        }
    }

    private bool _isAgcEnabled = true;
    public bool IsAgcEnabled
    {
        get => _isAgcEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAgcEnabled, value);
            if (_isInitialized) _respeaker.Write(19, 0, value ? 1 : 0);
        }
    }

    private bool _isNsEnabled = true;
    public bool IsNsEnabled
    {
        get => _isNsEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _isNsEnabled, value);
            if (_isInitialized) _respeaker.Write(19, 8, value ? 1 : 0);
        }
    }

    private bool _isAecEnabled;
    public bool IsAecEnabled
    {
        get => _isAecEnabled;
        set
        {
            if (_isAecEnabled == value) return;
            this.RaiseAndSetIfChanged(ref _isAecEnabled, value);
            if (!_isInitialized) return;
            if (value) _ = _aec.EnableAecAsync();
            else _ = _aec.DisableAecAsync();
        }
    }

    private int _maxBrightness = 31;
    public int MaxBrightness
    {
        get => _maxBrightness;
        set => this.RaiseAndSetIfChanged(ref _maxBrightness, value);
    }

    private int _brightness = 20;
    public int Brightness
    {
        get => _brightness;
        set
        {
            this.RaiseAndSetIfChanged(ref _brightness, value);
            if (_isInitialized) _respeaker.SetLedBrightness((byte)value);
        }
    }

    private string _ledPattern = "Trace";
    public string LedPattern
    {
        get => _ledPattern;
        set
        {
            this.RaiseAndSetIfChanged(ref _ledPattern, value);
            if (_isInitialized) UpdateLedMode();
        }
    }

    public void ReconnectRespeaker() => _respeaker.TryConnect();

    /// <summary>
    /// Updates the LED pattern on the ReSpeaker hardware.
    /// </summary>
    private void UpdateLedMode()
    {
        if (!_isInitialized) return;

        // Map the UI pattern name to the hardware mode index
        int mode = LedPattern switch
        {
            "Mono" => 1,
            "Listen" => 3,
            "Wait" => 4,
            "Think" => 5,
            "Speak" => 6,
            "Spin" => 7,
            "Off" => 8,
            _ => 0
        };

        if (LedPattern == "Off")
        {
            _respeaker.SetLedMono(0, 0, 0);
        }
        else if (LedPattern == "Mono")
        {
            // Default mono color is Red
            _respeaker.SetLedMono(255, 0, 0);
        }
        else
        {
            _respeaker.SetLedMode(mode);
        }
    }

    /// <summary>
    /// Scans the system for available audio input devices (microphones).
    /// </summary>
    private async void LoadMics()
    {
        var mics = await _audioCapture.GetAvailableSourcesAsync();
        AvailableMics.Clear();
        foreach (var mic in mics)
        {
            AvailableMics.Add(new MicDevice
            {
                Name = mic.Key,
                Description = string.IsNullOrEmpty(mic.Value) ? mic.Key : mic.Value
            });
        }
        // Automatically select the first found microphone
        SelectedMic = AvailableMics.FirstOrDefault();
    }

    /// <summary>
    /// Synchronizes the UI state with the current hardware register values 
    /// from the ReSpeaker device.
    /// </summary>
    private async void SyncHardware()
    {
        if (!_respeaker.IsConnected) return;

        // Read various audio enhancement states from the hardware registers
        _isFreezeEnabled = _respeaker.GetFreezeState() == 1;
        _isAgcEnabled = _respeaker.GetAgcState() == 1;
        _isNsEnabled = _respeaker.GetNsState() == 1;
        
        // AEC state is checked via the specialized AEC service
        _isAecEnabled = await _aec.IsAecLoadedAsync();
        
        // ReSpeaker v2.0 and Lite have different max brightness levels
        MaxBrightness = _respeaker.MaxBrightness;

        // Update UI properties
        this.RaisePropertyChanged(nameof(IsFreezeEnabled));
        this.RaisePropertyChanged(nameof(IsAgcEnabled));
        this.RaisePropertyChanged(nameof(IsNsEnabled));
        this.RaisePropertyChanged(nameof(IsAecEnabled));

        // Mark as initialized so future UI changes trigger hardware writes
        _isInitialized = true;
    }
}
