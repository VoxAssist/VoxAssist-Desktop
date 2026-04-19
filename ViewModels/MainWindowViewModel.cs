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
using System.Reflection;
using VoxAssist.Desktop.Views;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using VoxAssist.Desktop.Models;

namespace VoxAssist.Desktop.ViewModels;

public class ActionViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    private string _prompt = "";
    public string Prompt { get => _prompt; set => this.RaiseAndSetIfChanged(ref _prompt, value); }

    private string _hotkeyDisplay = "None";
    public string HotkeyDisplay { get => _hotkeyDisplay; set => this.RaiseAndSetIfChanged(ref _hotkeyDisplay, value); }

    private string _aiModel = "";
    public string AiModel { get => _aiModel; set => this.RaiseAndSetIfChanged(ref _aiModel, value); }

    public List<KeyCode> Hotkey { get; set; } = new();
}

public class AiProviderViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

    private string _hostUrl = "";
    public string HostUrl { get => _hostUrl; set => this.RaiseAndSetIfChanged(ref _hostUrl, value); }

    private string _apiKey = "";
    public string ApiKey { get => _apiKey; set => this.RaiseAndSetIfChanged(ref _apiKey, value); }
}

public class LlmViewModel : ViewModelBase
{
    private string _providerName = "";
    public string ProviderName { get => _providerName; set => this.RaiseAndSetIfChanged(ref _providerName, value); }

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
    public ActionViewModel? SelectedAction 
    { 
        get => _selectedAction; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _selectedAction, value); 
            this.RaisePropertyChanged(nameof(SelectedActionLlm));
            this.RaisePropertyChanged(nameof(IsPromptEnabled));
        } 
    }

    public LlmViewModel? SelectedActionLlm
    {
        get => LlmSelectorItems.FirstOrDefault(l => l.Model == SelectedAction?.AiModel || (l.Model == "Default" && string.IsNullOrEmpty(SelectedAction?.AiModel)));
        set
        {
            if (SelectedAction != null && value != null)
            {
                SelectedAction.AiModel = value.Model == "Default" ? "" : value.Model;
                this.RaisePropertyChanged(nameof(SelectedActionLlm));
                this.RaisePropertyChanged(nameof(IsPromptEnabled));
                SaveLocalData();
            }
        }
    }

    public bool IsPromptEnabled => SelectedAction?.AiModel != "None";
    
    public ObservableCollection<LlmViewModel> Llms { get; } = new();
    public ObservableCollection<LlmViewModel> LlmSelectorItems { get; } = new();
    public ObservableCollection<AiProviderViewModel> AiProviders { get; } = new();
    
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
            if (!string.IsNullOrEmpty(llm.Model) && llm.Model != "Default" && llm.Model != "None") EditLlm(llm);
        } 
    }

    public HotkeyService HotkeyService => _hotkey;

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
            var settingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings");
            if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);

            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName().Name;
            
            string[] files = { "settings.json", "ai_models.json", "actions.json", "ai_providers.json" };
            foreach (var file in files)
            {
                var filePath = Path.Combine(settingsDir, file);
                if (!File.Exists(filePath))
                {
                    var resourceName = $"{assemblyName}.Settings.{file}";
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        File.WriteAllText(filePath, reader.ReadToEnd());
                    }
                }
            }

            // 1. Load Providers
            var providersPath = Path.Combine(settingsDir, "ai_providers.json");
            if (File.Exists(providersPath))
            {
                var json = File.ReadAllText(providersPath);
                var providers = System.Text.Json.JsonSerializer.Deserialize<List<AiProviderConfig>>(json);
                if (providers != null)
                {
                    AiProviders.Clear();
                    foreach (var p in providers) AiProviders.Add(new AiProviderViewModel { Name = p.Name, HostUrl = p.HostUrl, ApiKey = p.ApiKey });
                }
            }

            // 2. Load Models
            var modelsPath = Path.Combine(settingsDir, "ai_models.json");
            if (File.Exists(modelsPath))
            {
                var json = File.ReadAllText(modelsPath);
                var models = System.Text.Json.JsonSerializer.Deserialize<List<LlmConfig>>(json);
                if (models != null)
                {
                    Llms.Clear();
                    foreach (var l in models)
                    {
                        Llms.Add(new LlmViewModel { ProviderName = l.ProviderName, Model = l.Model, IsDefault = l.IsDefault });
                    }
                }
            }

            // 3. Load Actions
            var actionsPath = Path.Combine(settingsDir, "actions.json");
            if (File.Exists(actionsPath))
            {
                var json = File.ReadAllText(actionsPath);
                var actions = System.Text.Json.JsonSerializer.Deserialize<List<ActionConfig>>(json);
                if (actions != null)
                {
                    Actions.Clear();
                    foreach (var a in actions)
                    {
                        var hotkey = string.IsNullOrEmpty(a.Hotkey) ? new List<KeyCode>() : a.Hotkey.Split(',').Select(s => (KeyCode)int.Parse(s)).ToList();
                        var hotkeyDisplay = hotkey.Count > 0 ? string.Join(" + ", hotkey.Select(k => k.ToString().Replace("Vc", ""))) : "None";
                        Actions.Add(new ActionViewModel { Name = a.Name, Prompt = a.Prompt, AiModel = a.AiModel, Hotkey = hotkey, HotkeyDisplay = hotkeyDisplay });
                    }
                }
            }

            // 4. Load General Settings
            var settingsPath = Path.Combine(settingsDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
                if (config != null)
                {
                    _isCcw = config.IsCcw;
                    _isGrokStt = config.IsGrokStt;
                    _grokApiKey = config.GrokApiKey;
                    _voxAssistHostUrl = config.VoxAssistHostUrl;
                }
            }
        }
        catch { }

        // Fallbacks
        if (AiProviders.Count == 0) AiProviders.Add(new AiProviderViewModel { Name = "xAI", HostUrl = "https://api.x.ai/v1" });
        if (Llms.Count == 0) Llms.Add(new LlmViewModel { ProviderName = "xAI", Model = "grok-4.20-non-reasoning", IsDefault = true });
        
        if (Actions.Count == 0)
        {
            Actions.Add(new ActionViewModel 
            { 
                Name = "Dictation", 
                Prompt = "You are a voice assistant, taking dictation. Put the spoken intent into \"Keyboard\", removing filled pauses and self-corrections, and set 'Markdown' to an empty string. Convert squences of numbers into numerals.\nDo not offer unsolicited advice or follow-up comments.\nReply ONLY with valid JSON containing \"Keyboard\" and \"Markdown\" keys.", 
                AiModel = "" 
            });
            Actions.Add(new ActionViewModel 
            { 
                Name = "Question", 
                Prompt = "You are a voice assistant, that helps with simple one-shot queries. Keep your answer brief, and put that into \"Markdown\"\nIf you are asked to spell something, then put that into \"Keyboard\", nouns first letter can be capitalised, otherwise all lower case.\nReply ONLY with valid JSON containing \"Keyboard\" and \"Markdown\" keys.\n", 
                AiModel = "" 
            });
        }
        
        UpdateLlmSelector();
        UpdateHotkeyService();
        Conversation.Insert(0, "System: Standalone mode initialized.");
    }

    private void UpdateLlmSelector()
    {
        LlmSelectorItems.Clear();
        LlmSelectorItems.Add(new LlmViewModel { Model = "Default", ProviderName = "" });
        LlmSelectorItems.Add(new LlmViewModel { Model = "None", ProviderName = "" });
        foreach (var llm in Llms) LlmSelectorItems.Add(llm);

        foreach (var action in Actions)
        {
            if (!string.IsNullOrEmpty(action.AiModel) && action.AiModel != "None" && !Llms.Any(l => l.Model == action.AiModel)) action.AiModel = "";
        }
    }

    private void UpdateHotkeyService()
    {
        var mapping = new Dictionary<int, List<KeyCode>>();
        for (int i = 0; i < Actions.Count; i++)
        {
            if (Actions[i].Hotkey.Count > 0) mapping.Add(i, Actions[i].Hotkey);
        }
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

    private void OnHotKeyPressed(int actionId) 
    { 
        if (actionId < 0 || actionId >= Actions.Count) return;
        var action = Actions[actionId];

        if (action.AiModel == "None")
        {
             // Start Recording directly, no AI check
            Status = $"Action: {action.Name}";
            MicStatus = "Listening...";
            _respeaker.SetLedMode(3); 
            _audioCapture.StartRecording(SelectedMic?.Name ?? "Default");
            return;
        }

        var model = Llms.FirstOrDefault(l => l.Model == action.AiModel);
        if (model == null) model = Llms.FirstOrDefault(l => l.IsDefault);

        if (model == null)
        {
            Dispatcher.UIThread.Post(() => Conversation.Insert(0, $"Error: No AI Model configured for '{action.Name}' and no Default model found."));
            return;
        }

        var provider = AiProviders.FirstOrDefault(p => p.Name == model.ProviderName);
        if (provider == null || string.IsNullOrEmpty(provider.HostUrl) || string.IsNullOrEmpty(provider.ApiKey))
        {
            Dispatcher.UIThread.Post(() => Conversation.Insert(0, $"Error: Provider '{model.ProviderName}' for model '{model.Model}' is missing Host URL or API Key."));
            return;
        }

        Status = $"Action: {action.Name}";
        MicStatus = "Listening...";
        _respeaker.SetLedMode(3); 
        _audioCapture.StartRecording(SelectedMic?.Name ?? "Default");
    }

    private void OnHotKeyReleased(int actionId) 
    { 
        if (!_audioCapture.IsRecording) return;
        var audio = _audioCapture.StopRecording();
        
        MicStatus = "Processing...";
        _respeaker.SetLedMode(5); 
        Status = "Ready";
    }

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
            SaveLocalData();
        }
    }

    public void AddAction() 
    { 
        var newAction = new ActionViewModel { Name = "New Action", Prompt = "You are a helpfull voice assistant", AiModel = "" }; 
        Actions.Add(newAction);
        SelectedAction = newAction;
        SaveLocalData();
    }
    
    public void SaveSelectedAction() { SaveLocalData(); }
    public void DeleteSelectedAction() { if (SelectedAction != null) { Actions.Remove(SelectedAction); SaveLocalData(); } }

    public async Task AddLlm()
    {
        var newLlm = new LlmViewModel { ProviderName = AiProviders.FirstOrDefault()?.Name ?? "", Model = "" };
        await EditLlm(newLlm, true);
    }

    public async Task AddProvider(Window? owner = null)
    {
        var newProvider = new AiProviderViewModel { Name = "New Provider", HostUrl = "" };
        var dialog = new ProviderEditDialog { DataContext = newProvider };
        var targetOwner = owner ?? GetMainWindow();
        if (targetOwner == null) return;

        var result = await dialog.ShowDialog<bool>(targetOwner);
        if (result)
        {
            AiProviders.Add(newProvider);
            SaveLocalData();
        }
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
        var dialog = new LlmEditDialog(this) { DataContext = llm, IsNew = isNew };
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
            var settingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings");
            if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);

            var config = new UserConfig { IsCcw = IsCcw, IsGrokStt = IsGrokStt, GrokApiKey = GrokApiKey, VoxAssistHostUrl = VoxAssistHostUrl };
            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), json);

            var providers = AiProviders.Select(p => new AiProviderConfig { Name = p.Name, HostUrl = p.HostUrl, ApiKey = p.ApiKey }).ToList();
            var providersJson = System.Text.Json.JsonSerializer.Serialize(providers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(settingsDir, "ai_providers.json"), providersJson);

            var actions = Actions.Select(a => new ActionConfig 
            { 
                Name = a.Name, 
                Prompt = a.Prompt, 
                AiModel = a.AiModel,
                Hotkey = string.Join(",", a.Hotkey.Select(k => (int)k))
            }).ToList();
            var actionsJson = System.Text.Json.JsonSerializer.Serialize(actions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(settingsDir, "actions.json"), actionsJson);

            var models = Llms.Select(l => new LlmConfig { ProviderName = l.ProviderName, Model = l.Model, IsDefault = l.IsDefault }).ToList();
            var modelsJson = System.Text.Json.JsonSerializer.Serialize(models, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(settingsDir, "ai_models.json"), modelsJson);
        }
        catch { }
    }

    public void ReconnectRespeaker() => _respeaker.TryConnect();
    public void Dispose() { if (_isDisposed) return; _isDisposed = true; _hotkey.Dispose(); _respeaker.Dispose(); }
}
