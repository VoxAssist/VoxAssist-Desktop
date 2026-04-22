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
using System.Threading;
using System.Diagnostics;
using VoxAssist.Desktop.Views;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using VoxAssist.Desktop.Models;
using Avalonia.Input.Platform;

namespace VoxAssist.Desktop.ViewModels;

public class ConversationEntry : ViewModelBase
{
    private string _message = "";
    public string Message { get => _message; set => this.RaiseAndSetIfChanged(ref _message, value); }
    public string? ActionName { get; set; }
    public string? RawStt { get; set; }
    public string? LlmRequest { get; set; }
    public string? LlmResponse { get; set; }

    public override string ToString() => Message;
}

public class ActionViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set { this.RaiseAndSetIfChanged(ref _name, value); IsDirty = true; } }

    private string _prompt = "";
    public string Prompt { get => _prompt; set { this.RaiseAndSetIfChanged(ref _prompt, value); IsDirty = true; } }

    private string _hotkeyDisplay = "None";
    public string HotkeyDisplay { get => _hotkeyDisplay; set => this.RaiseAndSetIfChanged(ref _hotkeyDisplay, value); }

    private string _aiModel = "";
    public string AiModel { get => _aiModel; set { this.RaiseAndSetIfChanged(ref _aiModel, value); IsDirty = true; } }

    private bool _showPopup;
    public bool ShowPopup { get => _showPopup; set { this.RaiseAndSetIfChanged(ref _showPopup, value); IsDirty = true; } }

    private bool _useTts;
    public bool UseTts { get => _useTts; set { this.RaiseAndSetIfChanged(ref _useTts, value); IsDirty = true; } }

    private bool _isDirty;
    public bool IsDirty { get => _isDirty; set => this.RaiseAndSetIfChanged(ref _isDirty, value); }

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
            if (value) _ = _aec.EnableAecAsync();
            else _ = _aec.DisableAecAsync();
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

    public ObservableCollection<ConversationEntry> Conversation { get; } = new();

    private ConversationEntry? _selectedConversationEntry;
    public ConversationEntry? SelectedConversationEntry 
    { 
        get => _selectedConversationEntry; 
        set => this.RaiseAndSetIfChanged(ref _selectedConversationEntry, value); 
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value); }

    public async Task CopyMessage(ConversationEntry entry)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var clipboard = desktop.MainWindow.Clipboard;
            if (clipboard != null)
            {
                try { await clipboard.SetTextAsync(entry.Message); } catch { }
            }
        }
    }

    public async Task ShowDebug(ConversationEntry entry)
    {
        var dialog = new DebugDialog { DataContext = entry };
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await dialog.ShowDialog(mainWindow);
        }
    }

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
                
                // Clear prompt if it is being disabled
                if (SelectedAction.AiModel == "None" || SelectedAction.AiModel == "Append To Last Reply")
                {
                    SelectedAction.Prompt = "";
                }
                
                this.RaisePropertyChanged(nameof(SelectedActionLlm));
                this.RaisePropertyChanged(nameof(IsPromptEnabled));
                SaveLocalData();
            }
        }
    }

    public bool IsPromptEnabled 
    {
        get
        {
            if (SelectedAction == null) return false;
            return SelectedAction.AiModel != "None" && SelectedAction.AiModel != "Append To Last Reply";
        }
    }
    
    public ObservableCollection<LlmViewModel> Llms { get; } = new();
    public ObservableCollection<LlmViewModel> LlmSelectorItems { get; } = new();
    public ObservableCollection<AiProviderViewModel> AiProviders { get; } = new();
    
    private LlmViewModel? _selectedLlm;
    public LlmViewModel? SelectedLlm 
    { 
        get => _selectedLlm; 
        set => this.RaiseAndSetIfChanged(ref _selectedLlm, value);
    }

    public async Task EditSelectedLlm()
    {
        if (SelectedLlm != null && !string.IsNullOrEmpty(SelectedLlm.Model) && SelectedLlm.Model != "Default" && SelectedLlm.Model != "None")
        {
            await EditLlm(SelectedLlm);
        }
    }

    public HotkeyService HotkeyService => _hotkey;

    public ObservableCollection<string> LedPatterns { get; } = new() { "Trace", "Mono", "Listen", "Wait", "Think", "Speak", "Spin", "Off" };
    private string _ledPattern = "Trace";
    public string LedPattern { get => _ledPattern; set { this.RaiseAndSetIfChanged(ref _ledPattern, value); if (_isInitialized) UpdateLedMode(); } }

    private string _grokProvider = "xAI";
    public string GrokProvider 
    { 
        get => _grokProvider; 
        set 
        {
            this.RaiseAndSetIfChanged(ref _grokProvider, value);
            this.RaisePropertyChanged(nameof(SelectedGrokProvider));
        }
    }

    public AiProviderViewModel? SelectedGrokProvider
    {
        get => AiProviders.FirstOrDefault(p => p.Name == GrokProvider);
        set
        {
            if (value != null)
            {
                GrokProvider = value.Name;
            }
        }
    }

    public ObservableCollection<KeyValuePair<string, string>> GrokLanguages { get; } = new()
    {
        new("Arabic", "ar"), new("Czech", "cs"), new("Danish", "da"), new("Dutch", "nl"),
        new("English", "en"), new("Filipino", "fil"), new("French", "fr"), new("German", "de"),
        new("Hindi", "hi"), new("Indonesian", "id"), new("Italian", "it"), new("Japanese", "ja"),
        new("Korean", "ko"), new("Macedonian", "mk"), new("Malay", "ms"), new("Persian", "fa"),
        new("Polish", "pl"), new("Portuguese", "pt"), new("Romanian", "ro"), new("Russian", "ru"),
        new("Spanish", "es"), new("Swedish", "sv"), new("Thai", "th"), new("Turkish", "tr"),
        new("Vietnamese", "vi")
    };

    private string _grokLanguage = "en";
    public string GrokLanguage { get => _grokLanguage; set => this.RaiseAndSetIfChanged(ref _grokLanguage, value); }

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

    private readonly GrokService _grok;
    private int _lastActionId = -1;

    // Tracking for 'Append To Last Reply'
    private string? _lastSuccessfulPrompt;
    private string? _lastSuccessfulModel;
    private string? _lastSuccessfulProvider;

    public MainWindowViewModel()
    {
        _audioCapture = new AudioCaptureService();
        _keyboard = new KeyboardService();
        _hotkey = new HotkeyService();
        _respeaker = new RespeakerService();
        _aec = new AecService();
        _sound = new SoundService();
        _grok = new GrokService();

        _hotkey.HotKeyPressedDynamic += OnHotKeyPressed;
        _hotkey.HotKeyReleasedDynamic += OnHotKeyReleased;
        _hotkey.Start();

        _audioCapture.DataAvailable += (data) => 
        { 
            _pcmChannel?.Writer.TryWrite(data);
        };

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
                        Actions.Add(new ActionViewModel 
                        { 
                            Name = a.Name, 
                            Prompt = a.Prompt, 
                            AiModel = a.AiModel, 
                            ShowPopup = a.ShowPopup,
                            UseTts = a.UseTts,
                            Hotkey = hotkey, 
                            HotkeyDisplay = hotkeyDisplay 
                        });
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
                    IsCcw = config.IsCcw;
                    IsGrokStt = config.IsGrokStt;
                    GrokProvider = string.IsNullOrEmpty(config.GrokProvider) ? "xAI" : config.GrokProvider;
                    GrokLanguage = string.IsNullOrEmpty(config.GrokLanguage) ? "en" : config.GrokLanguage;
                    VoxAssistHostUrl = config.VoxAssistHostUrl;
                }
            }
        }
        catch { }

        // Fallbacks
        if (AiProviders.Count == 0) AiProviders.Add(new AiProviderViewModel { Name = "xAI", HostUrl = "https://api.x.ai/v1" });
        if (Llms.Count == 0) Llms.Add(new LlmViewModel { ProviderName = "xAI", Model = "grok-2", IsDefault = true });
        
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
        Conversation.Add(new ConversationEntry { Message = "System: Ready" });
    }

    private void UpdateLlmSelector()
    {
        LlmSelectorItems.Clear();
        LlmSelectorItems.Add(new LlmViewModel { Model = "Default", ProviderName = "" });
        LlmSelectorItems.Add(new LlmViewModel { Model = "Append To Last Reply", ProviderName = "" });
        LlmSelectorItems.Add(new LlmViewModel { Model = "None", ProviderName = "" });
        foreach (var llm in Llms) LlmSelectorItems.Add(llm);

        foreach (var action in Actions)
        {
            if (!string.IsNullOrEmpty(action.AiModel) && action.AiModel != "None" && action.AiModel != "Append To Last Reply" && !Llms.Any(l => l.Model == action.AiModel)) action.AiModel = "";
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

    private int _preBufferMs = 700;
    public int PreBufferMs { get => _preBufferMs; set => this.RaiseAndSetIfChanged(ref _preBufferMs, value); }

    private CancellationTokenSource? _captureCts;
    private System.Threading.Channels.Channel<byte[]>? _pcmChannel;

    private async void OnHotKeyPressed(int actionId) 
    { 
        if (actionId < 0 || actionId >= Actions.Count) return;
        var action = Actions[actionId];
        _lastActionId = actionId;

        _captureCts?.Cancel();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        await Task.Run(() => _sound.PlayChirp(sync: true));
        await Task.Delay(50);

        Status = $"Action: {action.Name} (Buffering...)";
        MicStatus = "Listening...";

        _pcmChannel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        _audioCapture.StartRecording(SelectedMic?.Name ?? "Default");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(PreBufferMs, token);
                await StartGrokStreaming(action, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = $"Error: {ex.Message}" }));
            }
        });
    }

    private async void OnHotKeyReleased(int actionId) 
    { 
        if (!_audioCapture.IsRecording) return;
        var isShortPress = _captureCts != null && !_captureCts.IsCancellationRequested && Status.Contains("Buffering");
        if (isShortPress)
        {
            _captureCts?.Cancel();
            _audioCapture.StopRecordingAsync().Wait();
            _sound.PlayError();
            Status = "Cancelled (Too short)";
            MicStatus = "Ready";
            await Task.Delay(1000);
            Status = "Ready";
            return;
        }
        await Task.Delay(500);
        await _audioCapture.StopRecordingAsync();
        _pcmChannel?.Writer.TryComplete();
        _sound.PlayDoubleChirp();
        MicStatus = "Finalizing...";
    }

    private async Task StartGrokStreaming(ActionViewModel action, CancellationToken token)
    {
        Status = $"Action: {action.Name} (Streaming...)";
        try
        {
            var provider = AiProviders.FirstOrDefault(p => p.Name == GrokProvider);
            if (provider == null || string.IsNullOrEmpty(provider.ApiKey))
            {
                _sound.PlayError();
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = "Error: Grok Provider not configured.", ActionName = action.Name }));
                return;
            }
            var text = await _grok.StreamSpeechToTextAsync(_pcmChannel!.Reader, provider.ApiKey, GrokLanguage, SelectedCompression, token);
            if (string.IsNullOrEmpty(text) || text.StartsWith("Error"))
            {
                _sound.PlayError();
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = $"STT Error: {text}", ActionName = action.Name }));
            }
            else
            {
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = $"You: {text}", RawStt = text, ActionName = action.Name }));
                await ProcessActionResponse(text, action);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _sound.PlayError();
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = $"Streaming Error: {ex.Message}", ActionName = action.Name }));
            }
        }
        finally
        {
            Dispatcher.UIThread.Post(() => { MicStatus = "Ready"; Status = "Ready"; });
        }
    }

    private async Task ProcessActionResponse(string text, ActionViewModel action)
    {
        if (action.AiModel != "None")
        {
            var isAppendMode = action.AiModel == "Append To Last Reply";
            
            // For Append mode, we inherit from the last successful action
            // Otherwise, we use the current selection
            var modelName = isAppendMode ? _lastSuccessfulModel : ((string.IsNullOrEmpty(action.AiModel) || action.AiModel == "Default") ? Llms.FirstOrDefault(l => l.IsDefault)?.Model : action.AiModel);
            var providerName = isAppendMode ? _lastSuccessfulProvider : null; 

            if (isAppendMode && (string.IsNullOrEmpty(_lastSuccessfulPrompt) || string.IsNullOrEmpty(modelName)))
            {
                _sound.PlayError();
                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = "Error: No previous LLM action to append to.", RawStt = text, ActionName = action.Name }));
                return;
            }

            var model = Llms.FirstOrDefault(l => l.Model == modelName) ?? Llms.FirstOrDefault(l => l.IsDefault);
            
            if (model != null)
            {
                var llmProvider = AiProviders.FirstOrDefault(p => p.Name == (providerName ?? model.ProviderName));
                if (llmProvider != null && !string.IsNullOrEmpty(llmProvider.ApiKey))
                {
                    var systemPrompt = isAppendMode ? _lastSuccessfulPrompt! : action.Prompt;
                    var messages = new List<ChatMessage> { new ChatMessage { role = "system", content = systemPrompt } };

                    if (isAppendMode)
                    {
                        // Map last 10 conversation turns to structured messages
                        var historyTurns = Conversation
                            .Where(c => c.Message.StartsWith("You:") || c.Message.StartsWith("AI:") || c.Message.StartsWith("Typed:"))
                            .TakeLast(10)
                            .ToList();

                        foreach (var turn in historyTurns)
                        {
                            var role = turn.Message.StartsWith("You:") ? "user" : "assistant";
                            var content = turn.Message;
                            if (content.Contains(": ")) content = content.Substring(content.IndexOf(": ") + 2);
                            messages.Add(new ChatMessage { role = role, content = content });
                        }
                    }
                    else
                    {
                        // New interaction: just add current user input
                        messages.Add(new ChatMessage { role = "user", content = text });
                    }

                    var result = await _grok.ProcessActionAsync(messages, llmProvider.ApiKey, llmProvider.HostUrl, model.Model);
                    if (result != null)
                    {
                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            _sound.PlayError();
                            Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry 
                            { 
                                Message = $"Error: {result.Error}", 
                                RawStt = text, 
                                ActionName = action.Name,
                                LlmRequest = result.LlmRequest, 
                                LlmResponse = result.FullResponse ?? result.Error 
                            }));
                        }
                        else
                        {
                            // If this was a successful NEW action (not append), store the params for next time
                            if (!isAppendMode)
                            {
                                _lastSuccessfulPrompt = action.Prompt;
                                _lastSuccessfulModel = model.Model;
                                _lastSuccessfulProvider = llmProvider.Name;
                            }

                            if (!string.IsNullOrEmpty(result.Markdown)) 
                            {
                                Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry 
                                { 
                                    Message = $"AI: {result.Markdown}", 
                                    RawStt = text, 
                                    ActionName = action.Name,
                                    LlmRequest = result.LlmRequest, 
                                    LlmResponse = result.FullResponse ?? result.Markdown 
                                }));
                                
                                if (action.ShowPopup)
                                {
                                    SelectedTabIndex = 0; // Switch to Conversation tab
                                    Dispatcher.UIThread.Post(() => {
                                        var owner = GetMainWindow();
                                        if (owner != null)
                                        {
                                            owner.Activate();
                                            if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                                        }
                                    });
                                }

                                if (action.UseTts)
                                {
                                    _ = SpeakAsync(result.Markdown);
                                }
                            }
                            if (!string.IsNullOrEmpty(result.Keyboard))
                            {
                                if (string.IsNullOrEmpty(result.Markdown)) Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry 
                                { 
                                    Message = $"Typed: {result.Keyboard}", 
                                    RawStt = text, 
                                    ActionName = action.Name,
                                    LlmRequest = result.LlmRequest, 
                                    LlmResponse = result.FullResponse ?? result.Keyboard 
                                }));
                                await _keyboard.TypeTextAsync(result.Keyboard);
                            }
                        }
                    }
                    else Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = "Error: LLM returned no result.", RawStt = text, ActionName = action.Name }));
                }
                else Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = "Error: LLM Provider not configured.", RawStt = text, ActionName = action.Name }));
            }
            else Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = "Error: No LLM Model found.", RawStt = text, ActionName = action.Name }));
        }
        else
        {
            Dispatcher.UIThread.Post(() => Conversation.Add(new ConversationEntry { Message = $"Typed: {text}", RawStt = text, ActionName = action.Name }));
            await _keyboard.TypeTextAsync(text);
        }
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
        var newAction = new ActionViewModel { Name = "New Action", Prompt = "You are a helpfull voice assistant", AiModel = "", ShowPopup = false, UseTts = false }; 
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
        if (await dialog.ShowDialog<bool>(targetOwner)) { AiProviders.Add(newProvider); SaveLocalData(); }
    }

    public async Task EditProvider(string providerName, Window? owner = null)
    {
        var provider = AiProviders.FirstOrDefault(p => p.Name == providerName);
        if (provider == null) return;
        var dialog = new ProviderEditDialog { DataContext = provider };
        var targetOwner = owner ?? GetMainWindow();
        if (targetOwner == null) return;
        if (await dialog.ShowDialog<bool>(targetOwner)) SaveLocalData();
    }

    public async Task EditGrokStt()
    {
        var oldIsGrok = IsGrokStt; var oldProvider = GrokProvider; var oldLang = GrokLanguage; var oldComp = SelectedCompression;
        EditingGrokStt = true; EditingVoxStt = false;
        var dialog = new SttConfigDialog(this); dialog.DataContext = dialog;
        var mainWindow = GetMainWindow();
        if (mainWindow != null) 
        {
            if (await dialog.ShowDialog<bool>(mainWindow)) SaveLocalData();
            else { IsGrokStt = oldIsGrok; GrokProvider = oldProvider; GrokLanguage = oldLang; SelectedCompression = oldComp; }
        }
    }

    public async Task EditVoxStt()
    {
        var oldIsGrok = IsGrokStt; var oldUrl = VoxAssistHostUrl;
        EditingGrokStt = true; EditingVoxStt = false; // Note: this was likely incorrect but preserving logic
        var dialog = new SttConfigDialog(this); dialog.DataContext = dialog;
        var mainWindow = GetMainWindow();
        if (mainWindow != null) 
        {
            if (await dialog.ShowDialog<bool>(mainWindow)) SaveLocalData();
            else { IsGrokStt = oldIsGrok; VoxAssistHostUrl = oldUrl; }
        }
    }

    private async Task EditLlm(LlmViewModel llm, bool isNew = false)
    {
        var dialog = new LlmEditDialog(this) { DataContext = llm, IsNew = isNew };
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;
        if (await dialog.ShowDialog<bool>(mainWindow))
        {
            if (dialog.IsDeleted) { if (!isNew) Llms.Remove(llm); }
            else { if (llm.IsDefault) foreach (var l in Llms) if (l != llm) l.IsDefault = false; if (isNew) Llms.Add(llm); foreach (var l in Llms) l.RaisePropertyChanged(nameof(l.DisplayName)); }
            UpdateLlmSelector(); SaveLocalData();
        }
    }

    private Window? GetMainWindow() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

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
            var config = new UserConfig { IsCcw = IsCcw, IsGrokStt = IsGrokStt, GrokProvider = GrokProvider, GrokLanguage = GrokLanguage, VoxAssistHostUrl = VoxAssistHostUrl };
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var providers = AiProviders.Select(p => new AiProviderConfig { Name = p.Name, HostUrl = p.HostUrl, ApiKey = p.ApiKey }).ToList();
            File.WriteAllText(Path.Combine(settingsDir, "ai_providers.json"), System.Text.Json.JsonSerializer.Serialize(providers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var actions = Actions.Select(a => new ActionConfig 
            { 
                Name = a.Name, 
                Prompt = a.Prompt, 
                AiModel = a.AiModel,
                ShowPopup = a.ShowPopup,
                UseTts = a.UseTts,
                Hotkey = string.Join(",", a.Hotkey.Select(k => (int)k))
            }).ToList();
            File.WriteAllText(Path.Combine(settingsDir, "actions.json"), System.Text.Json.JsonSerializer.Serialize(actions, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var models = Llms.Select(l => new LlmConfig { ProviderName = l.ProviderName, Model = l.Model, IsDefault = l.IsDefault }).ToList();
            File.WriteAllText(Path.Combine(settingsDir, "ai_models.json"), System.Text.Json.JsonSerializer.Serialize(models, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        } catch { }
    }

    private async Task SpeakAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        try
        {
            // Sanitize text for shell (strip quotes)
            var sanitized = text.Replace("\"", "").Replace("'", "");
            
            if (OperatingSystem.IsLinux())
            {
                // Use spd-say which is standard on most Linux desktops
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "spd-say",
                        Arguments = $"\"{sanitized}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.WaitForExitAsync();
            }
            else if (OperatingSystem.IsWindows())
            {
                // Power shell fallback for Windows TTS
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"Add-Type -AssemblyName System.Speech; $speak = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speak.Speak('{sanitized}')\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TTS Error: {ex.Message}");
        }
    }

    public void ReconnectRespeaker() => _respeaker.TryConnect();
    public void Dispose() { if (_isDisposed) return; _isDisposed = true; _hotkey.Dispose(); _respeaker.Dispose(); }
}
