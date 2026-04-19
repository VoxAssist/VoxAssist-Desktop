using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using VoxAssist.Desktop.Services;
using SharpHook.Native;
using SharpHook.Data;
using Avalonia.Threading;
using System.Net.Http;
using System.Net;
using System.IO;
using VoxAssist.Desktop.Views;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using VoxAssist.Desktop.Models;

namespace VoxAssist.Desktop.ViewModels;

public class ActionViewModel : ViewModelBase
{
    public Guid Id { get; set; }

    private string _name = "";
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    private string _prompt = "";
    public string Prompt { get => _prompt; set => this.RaiseAndSetIfChanged(ref _prompt, value); }

    private string _hotkeyDisplay = "None";
    public string HotkeyDisplay { get => _hotkeyDisplay; set => this.RaiseAndSetIfChanged(ref _hotkeyDisplay, value); }

    private bool _isSystemDefault = true;
    public bool IsSystemDefault { get => _isSystemDefault; set => this.RaiseAndSetIfChanged(ref _isSystemDefault, value); }

    private Guid _llmId;
    public Guid LlmId { get => _llmId; set => this.RaiseAndSetIfChanged(ref _llmId, value); }

    public List<KeyCode> Hotkey { get; set; } = new();
}

public class LlmViewModel : ViewModelBase
{
    public Guid Id { get; set; }
    
    private string _hostUrl = "";
    public string HostUrl { get => _hostUrl; set => this.RaiseAndSetIfChanged(ref _hostUrl, value); }

    private string _apiKey = "";
    public string ApiKey { get => _apiKey; set => this.RaiseAndSetIfChanged(ref _apiKey, value); }

    private string _model = "";
    public string Model { get => _model; set => this.RaiseAndSetIfChanged(ref _model, value); }

    private bool _isDefault;
    public bool IsDefault { get => _isDefault; set => this.RaiseAndSetIfChanged(ref _isDefault, value); }

    public string DisplayName => Model + (IsDefault ? " (default)" : "");
}

public class MicDevice
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public override string ToString() => Description;
}

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private readonly KeyboardService _keyboard;
    private readonly HotkeyService _hotkey;
    private readonly RespeakerService _respeaker;
    private readonly AecService _aec;
    private readonly SoundService _sound;
    private bool _isDisposed;

    private string _status = "Ready";
    public string Status { get => _status; set => this.RaiseAndSetIfChanged(ref _status, value); }

    private bool _isRespeakerConnected;
    public bool IsRespeakerConnected { get => _isRespeakerConnected; set => this.RaiseAndSetIfChanged(ref _isRespeakerConnected, value); }

    private int _doaAngle;
    public int DoaAngle { get => _doaAngle; set => this.RaiseAndSetIfChanged(ref _doaAngle, value); }

    private bool _isCcw;
    public bool IsCcw 
    { 
        get => _isCcw; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _isCcw, value); 
            SaveLocalData();
        } 
    }

    private bool _isInitialized = false;

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
            if (value) _aec.EnableAecAsync();
            else _aec.DisableAecAsync();
        }
    }

    private int _maxBrightness = 31;
    public int MaxBrightness { get => _maxBrightness; set => this.RaiseAndSetIfChanged(ref _maxBrightness, value); }

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

    public ObservableCollection<string> Conversation { get; } = new();

    private string _micStatus = "Ready";
    public string MicStatus { get => _micStatus; set => this.RaiseAndSetIfChanged(ref _micStatus, value); }

    public ObservableCollection<MicDevice> AvailableMics { get; } = new();
    
    private MicDevice? _selectedMic;
    public MicDevice? SelectedMic { get => _selectedMic; set => this.RaiseAndSetIfChanged(ref _selectedMic, value); }

    public ObservableCollection<CompressionType> CompressionTypes { get; } = new() 
    { 
        CompressionType.None, CompressionType.G711, CompressionType.Flac 
    };

    private CompressionType _selectedCompression = CompressionType.None;
    public CompressionType SelectedCompression { get => _selectedCompression; set => this.RaiseAndSetIfChanged(ref _selectedCompression, value); }

    public ObservableCollection<ActionViewModel> Actions { get; } = new();
    
    private ActionViewModel? _selectedAction;
    public ActionViewModel? SelectedAction { get => _selectedAction; set => this.RaiseAndSetIfChanged(ref _selectedAction, value); }
    
    public ObservableCollection<LlmViewModel> Llms { get; } = new();
    public ObservableCollection<LlmViewModel> LlmSelectorItems { get; } = new();
    
    private LlmViewModel? _selectedLlm;
    public LlmViewModel? SelectedLlm 
    { 
        get => _selectedLlm; 
        set 
        {
            if (value == null) return;
            var llm = value;
            _selectedLlm = null;
            this.RaisePropertyChanged(nameof(SelectedLlm));
            if (llm.Id != Guid.Empty) EditLlm(llm);
        } 
    }

    public HotkeyService HotkeyService => _hotkey;
    private Dictionary<Guid, List<KeyCode>> _actionHotkeys = new();

    public ObservableCollection<string> LedPatterns { get; } = new() { "Trace", "Mono", "Listen", "Wait", "Think", "Speak", "Spin", "Off" };
    private string _ledPattern = "Trace";
    public string LedPattern { get => _ledPattern; set { this.RaiseAndSetIfChanged(ref _ledPattern, value); if (_isInitialized) UpdateLedMode(); } }

    private string _grokApiKey = "";
    public string GrokApiKey { get => _grokApiKey; set => this.RaiseAndSetIfChanged(ref _grokApiKey, value); }

    private string _voxAssistHostUrl = "";
    public string VoxAssistHostUrl { get => _voxAssistHostUrl; set => this.RaiseAndSetIfChanged(ref _voxAssistHostUrl, value); }

    private bool _isGrokStt = true;
    public bool IsGrokStt 
    { 
        get => _isGrokStt; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _isGrokStt, value); 
            SaveLocalData();
        } 
    }

    private bool _editingGrokStt;
    public bool EditingGrokStt { get => _editingGrokStt; set => this.RaiseAndSetIfChanged(ref _editingGrokStt, value); }

    private bool _editingVoxStt;
    public bool EditingVoxStt { get => _editingVoxStt; set => this.RaiseAndSetIfChanged(ref _editingVoxStt, value); }

    public MainWindowViewModel()
    {
        _audioCapture = new AudioCaptureService();
        _keyboard = new KeyboardService();
        _hotkey = new HotkeyService();
        _respeaker = new RespeakerService();
        _aec = new AecService();
        _sound = new SoundService();

        _hotkey.HotKeyPressedDynamic += OnHotKeyPressed;
        _hotkey.HotKeyReleasedDynamic += OnHotKeyReleased;
        _hotkey.Start();

        _audioCapture.DataAvailable += async (data) => { /* Process locally */ };

        SyncHardware();
        LoadMics();
        LoadLocalData();

        DispatcherTimer.Run(() => {
            IsRespeakerConnected = _respeaker.IsConnected;
            if (IsRespeakerConnected) DoaAngle = _respeaker.GetDoaAngle();
            else _respeaker.TryConnect();
            return true;
        }, TimeSpan.FromSeconds(2));
    }

    private void LoadLocalData()
    {
        try
        {
            if (File.Exists("settings.json"))
            {
                var json = File.ReadAllText("settings.json");
                var config = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
                if (config != null)
                {
                    _isCcw = config.IsCcw;
                    _isGrokStt = config.IsGrokStt;
                    _grokApiKey = config.GrokApiKey;
                    _voxAssistHostUrl = config.VoxAssistHostUrl;

                    Actions.Clear();
                    foreach (var a in config.Actions)
                    {
                        Actions.Add(new ActionViewModel 
                        { 
                            Id = a.Id, 
                            Name = a.Name, 
                            Prompt = a.Prompt, 
                            LlmId = a.LlmId,
                            HotkeyDisplay = "None"
                        });
                    }

                    Llms.Clear();
                    foreach (var l in config.Llms)
                    {
                        Llms.Add(new LlmViewModel 
                        { 
                            Id = l.Id, 
                            HostUrl = l.HostUrl, 
                            ApiKey = l.ApiKey, 
                            Model = l.Model, 
                            IsDefault = l.IsDefault 
                        });
                    }
                }
            }
        }
        catch { }

        if (Llms.Count == 0)
        {
            var defaultLlm = new LlmViewModel { Id = Guid.NewGuid(), HostUrl = "https://api.openai.com/v1", Model = "gpt-4o", IsDefault = true };
            Llms.Add(defaultLlm);
        }
        
        if (Actions.Count == 0)
        {
            Actions.Add(new ActionViewModel { Id = Guid.NewGuid(), Name = "Example Action", Prompt = "You are a helpfull voice assistant", LlmId = Guid.Empty });
        }
        
        UpdateLlmSelector();
        UpdateHotkeyService();
        Conversation.Insert(0, "System: Standalone mode initialized.");
    }

    private void UpdateLlmSelector()
    {
        LlmSelectorItems.Clear();
        LlmSelectorItems.Add(new LlmViewModel { Id = Guid.Empty, Model = "Default" });
        foreach (var llm in Llms) LlmSelectorItems.Add(llm);

        // Check if actions still have valid LLMs
        foreach (var action in Actions)
        {
            if (action.LlmId != Guid.Empty && !Llms.Any(l => l.Id == action.LlmId))
            {
                action.LlmId = Guid.Empty;
            }
        }
    }

    private void UpdateHotkeyService()
    {
        var mapping = new Dictionary<int, List<KeyCode>>();
        _hotkey.UpdateHotkeys(mapping);
    }

    private async void LoadMics()
    {
        var mics = await _audioCapture.GetAvailableSourcesAsync();
        AvailableMics.Clear();
        foreach (var mic in mics) AvailableMics.Add(new MicDevice { Name = mic.Key, Description = string.IsNullOrEmpty(mic.Value) ? mic.Key : mic.Value });
        SelectedMic = AvailableMics.FirstOrDefault();
    }

    private async void SyncHardware()
    {
        if (!_respeaker.IsConnected) return;
        _isFreezeEnabled = _respeaker.GetFreezeState() == 1;
        _isAgcEnabled = _respeaker.GetAgcState() == 1;
        _isNsEnabled = _respeaker.GetNsState() == 1;
        _isAecEnabled = await _aec.IsAecLoadedAsync();
        MaxBrightness = _respeaker.MaxBrightness;
        this.RaisePropertyChanged(nameof(IsFreezeEnabled));
        this.RaisePropertyChanged(nameof(IsAgcEnabled));
        this.RaisePropertyChanged(nameof(IsNsEnabled));
        this.RaisePropertyChanged(nameof(IsAecEnabled));
        _isInitialized = true;
    }

    private async void OnHotKeyPressed(int actionId) { }
    private async void OnHotKeyReleased(int actionId) { }

    public async Task SetActionHotkey()
    {
        if (SelectedAction == null) return;
        var window = new HotkeyWindow(_hotkey, SelectedAction.Name);
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var result = await window.ShowDialog<List<KeyCode>>(mainWindow);
        if (result != null && result.Count > 0)
        {
            SelectedAction.Hotkey = result;
            SelectedAction.HotkeyDisplay = string.Join(" + ", result.Select(k => k.ToString().Replace("Vc", "")));
            UpdateHotkeyService();
        }
    }

    public void AddAction() 
    { 
        var newAction = new ActionViewModel { Id = Guid.NewGuid(), Name = "New Action", Prompt = "You are a helpfull voice assistant", LlmId = Guid.Empty }; 
        Actions.Add(newAction);
        SelectedAction = newAction;
        SaveLocalData();
    }
    
    public void SaveSelectedAction() { SaveLocalData(); }
    public void DeleteSelectedAction() { if (SelectedAction != null) { Actions.Remove(SelectedAction); SaveLocalData(); } }

    public async Task AddLlm()
    {
        var newLlm = new LlmViewModel { Id = Guid.NewGuid(), HostUrl = "", Model = "" };
        await EditLlm(newLlm, true);
    }

    public async Task EditGrokStt()
    {
        EditingGrokStt = true;
        EditingVoxStt = false;
        var dialog = new SttConfigDialog { DataContext = this };
        var mainWindow = GetMainWindow();
        if (mainWindow != null) 
        {
            await dialog.ShowDialog<bool>(mainWindow);
            SaveLocalData();
        }
    }

    public async Task EditVoxStt()
    {
        EditingGrokStt = false;
        EditingVoxStt = true;
        var dialog = new SttConfigDialog { DataContext = this };
        var mainWindow = GetMainWindow();
        if (mainWindow != null) 
        {
            await dialog.ShowDialog<bool>(mainWindow);
            SaveLocalData();
        }
    }

    private async Task EditLlm(LlmViewModel llm, bool isNew = false)
    {
        var dialog = new LlmEditDialog { DataContext = llm, IsNew = isNew };
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var result = await dialog.ShowDialog<bool>(mainWindow);
        if (result)
        {
            if (dialog.IsDeleted)
            {
                if (!isNew) Llms.Remove(llm);
            }
            else
            {
                if (llm.IsDefault)
                {
                    foreach (var l in Llms) if (l != llm) l.IsDefault = false;
                }
                if (isNew) Llms.Add(llm);
                foreach (var l in Llms) l.RaisePropertyChanged(nameof(l.DisplayName));
            }
            UpdateLlmSelector();
            SaveLocalData();
        }
    }

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private void UpdateLedMode()
    {
        if (!_isInitialized) return;
        int mode = LedPattern switch { "Mono" => 1, "Listen" => 3, "Wait" => 4, "Think" => 5, "Speak" => 6, "Spin" => 7, "Off" => 8, _ => 0 };
        if (LedPattern == "Off") _respeaker.SetLedMono(0,0,0);
        else if (LedPattern == "Mono") _respeaker.SetLedMono(255,0,0);
        else _respeaker.SetLedMode(mode);
    }

    private void SaveLocalData()
    {
        try
        {
            var config = new UserConfig
            {
                IsCcw = IsCcw,
                IsGrokStt = IsGrokStt,
                GrokApiKey = GrokApiKey,
                VoxAssistHostUrl = VoxAssistHostUrl,
                Actions = Actions.Select(a => new ActionConfig 
                { 
                    Id = a.Id, 
                    Name = a.Name, 
                    Prompt = a.Prompt, 
                    LlmId = a.LlmId 
                }).ToList(),
                Llms = Llms.Select(l => new LlmConfig 
                { 
                    Id = l.Id, 
                    HostUrl = l.HostUrl, 
                    ApiKey = l.ApiKey, 
                    Model = l.Model, 
                    IsDefault = l.IsDefault 
                }).ToList()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("settings.json", json);
        }
        catch { }
    }

    public void ReconnectRespeaker() => _respeaker.TryConnect();
    public void Dispose() { if (_isDisposed) return; _isDisposed = true; _hotkey.Dispose(); _respeaker.Dispose(); }
}
