using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using VoxAssist.Desktop.Services;
using SharpHook.Native;
using SharpHook.Data;
using System.Text;
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

namespace VoxAssist.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AudioCaptureService _audioCapture;
    private readonly KeyboardService _keyboard;
    private readonly HotkeyService _hotkey;
    private readonly RespeakerService _respeaker;
    private readonly AecService _aec;
    private readonly SoundService _sound;
    private readonly UpdateService _updater;
    private bool _isDisposed;

    private string _status = "Ready";
    public string Status { get => _status; set => this.RaiseAndSetIfChanged(ref _status, value); }

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

    public ObservableCollection<InteractionRecord> Conversation { get; } = new();

    private InteractionRecord? _selectedConversationEntry;
    public InteractionRecord? SelectedConversationEntry
    {
        get => _selectedConversationEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedConversationEntry, value);
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value); }

    public async Task HandleTabChangeWithUnsavedChanges(int newIndex)
    {
        var result = await CheckUnsavedChangesAsync();
        if (result == "Cancel")
        {
            return;
        }

        if (result == "Save") SaveSelectedAction();
        else if (result == "Discard") CancelSelectedAction();

        SelectedTabIndex = newIndex;
    }

    private async Task<string> CheckUnsavedChangesAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return "Discard";

        var dialog = new SaveConfirmationDialog();
        var result = await dialog.ShowDialog<string>(mainWindow);
        return result ?? "Cancel";
    }

    public async Task CopyMessage(InteractionRecord entry)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var clipboard = desktop.MainWindow.Clipboard;
            if (clipboard != null)
            {
                try { await clipboard.SetTextAsync(entry.DisplayMarkdown); } catch { }
            }
        }
    }

    public async Task SpeakInteraction(InteractionRecord entry)
    {
        if (entry == null) return;
        // Priority: AI Response > User Speech > Typed Text > System Text
        var text = entry.LlmMarkdown ?? entry.RawStt ?? entry.TypedText ?? entry.SystemText ?? "";
        if (!string.IsNullOrEmpty(text))
        {
            await SpeakTtsAsync(text, null);
        }
    }

    public async Task ShowDebug(InteractionRecord entry)
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

    public ObservableCollection<string> GrokTtsVoices { get; } = new() { "eve", "caleb", "viona", "lyra", "jace" };
    private string _grokTtsVoice = "eve";
    public string GrokTtsVoice { get => _grokTtsVoice; set => this.RaiseAndSetIfChanged(ref _grokTtsVoice, value); }

    private string _voxAssistHostUrl = "";
    public string VoxAssistHostUrl { get => _voxAssistHostUrl; set => this.RaiseAndSetIfChanged(ref _voxAssistHostUrl, value); }

    private int _maxTtsLength = 600;
    public int MaxTtsLength
    {
        get => _maxTtsLength;
        set
        {
            this.RaiseAndSetIfChanged(ref _maxTtsLength, value);
            SaveLocalData();
        }
    }

    private bool _isGrokStt = true;
    public bool IsGrokStt
    {
        get => _isGrokStt;
        set { if (value) SetSttMode(true, false, false); }
    }

    private bool _isGrokWebsocketStt;
    public bool IsGrokWebsocketStt
    {
        get => _isGrokWebsocketStt;
        set { if (value) SetSttMode(false, true, false); }
    }

    private bool _isVoxStt;
    public bool IsVoxStt
    {
        get => _isVoxStt;
        set { if (value) SetSttMode(false, false, true); }
    }

    private void SetSttMode(bool isGrok, bool isGrokWs, bool isVox)
    {
        if (_isGrokStt == isGrok && _isGrokWebsocketStt == isGrokWs && _isVoxStt == isVox)
            return;

        this.RaiseAndSetIfChanged(ref _isGrokStt, isGrok, nameof(IsGrokStt));
        this.RaiseAndSetIfChanged(ref _isGrokWebsocketStt, isGrokWs, nameof(IsGrokWebsocketStt));
        this.RaiseAndSetIfChanged(ref _isVoxStt, isVox, nameof(IsVoxStt));
        SaveLocalData();
    }

    private bool _editingGrokStt;
    public bool EditingGrokStt { get => _editingGrokStt; set => this.RaiseAndSetIfChanged(ref _editingGrokStt, value); }

    private bool _editingVoxStt;
    public bool EditingVoxStt { get => _editingVoxStt; set => this.RaiseAndSetIfChanged(ref _editingVoxStt, value); }

    private UpdateInfo? _availableUpdate;
    public UpdateInfo? AvailableUpdate { get => _availableUpdate; set => this.RaiseAndSetIfChanged(ref _availableUpdate, value); }

    private double _updateProgress;
    public double UpdateProgress { get => _updateProgress; set => this.RaiseAndSetIfChanged(ref _updateProgress, value); }

    private string _version = "1.0.0";
    public string VersionString => $"v{_version}";

    private DateTime _lastUpdateCheck = DateTime.MinValue;

    private readonly GrokService _grok;
    private readonly GrokTtsService _grokTts;
    private int _lastActionId = -1;
    private InteractionRecord? _activeRecord;
    private Stopwatch _sttLatencySw = new();

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
        _grokTts = new GrokTtsService();
        _updater = new UpdateService();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null) _version = $"{version.Major}.{version.Minor}.{version.Build}";

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
        CheckForUpdatesOnStart();

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await Dispatcher.UIThread.InvokeAsync(CheckLinuxPermissions);
        });

        DispatcherTimer.Run(() =>
        {
            IsRespeakerConnected = _respeaker.IsConnected;
            if (IsRespeakerConnected) DoaAngle = _respeaker.GetDoaAngle();
            else _respeaker.TryConnect();
            return true;
        }, TimeSpan.FromSeconds(2));
    }

    private void CheckLinuxPermissions()
    {
        if (PermissionService.IsLinuxAndMissingCapabilities())
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var mainWindow = GetMainWindow();
                if (mainWindow != null)
                {
                    var dialog = new PermissionDialog();
                    var result = await dialog.ShowDialog<bool>(mainWindow);
                    if (result)
                    {
                        await RequestElevation();
                    }
                }
                else
                {
                    Status = "Linux: Missing /dev/uinput permissions. Click here to fix.";
                }
            });
        }
    }

    public async Task RequestElevation()
    {
        Status = "Requesting elevation...";
        bool success = await PermissionService.ElevateAndRestartAsync();
        if (!success)
        {
            Status = "Elevation failed or cancelled.";
            await Task.Delay(3000);
            Status = "Ready";
        }
    }

    private int _preBufferMs = 700;
    public int PreBufferMs { get => _preBufferMs; set => this.RaiseAndSetIfChanged(ref _preBufferMs, value); }
    private CancellationTokenSource? _captureCts;
    private System.Threading.Channels.Channel<byte[]>? _pcmChannel;
    private CancellationTokenSource? _tickingCts;

    private void StartTicking()
    {
        StopTicking();
        _tickingCts = new CancellationTokenSource();
        var token = _tickingCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token);
                    if (!token.IsCancellationRequested)
                    {
                        _sound.PlayTick();
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void StopTicking()
    {
        _tickingCts?.Cancel();
        _tickingCts?.Dispose();
        _tickingCts = null;
    }

    /// <summary>
    /// Handles the recording of a global hotkey.
    /// This starts the audio capture and prepares for streaming.
    /// </summary>
    private async void OnHotKeyPressed(int actionId)
    {
        if (actionId < 0 || actionId >= Actions.Count) return;
        var action = Actions[actionId];
        _lastActionId = actionId;
        
        // Cancel any existing capture or ticking
        _captureCts?.Cancel();
        StopTicking();
        
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;
        
        // Audio feedback: start chirp
        await Task.Run(() => _sound.PlayChirp(sync: true));
        await Task.Delay(50);
        
        Status = $"Action: {action.Name} (Buffering...)";
        MicStatus = "Listening...";
        
        // Create the PCM stream channel for streaming to the STT provider
        _pcmChannel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        _audioCapture.StartRecording(SelectedMic?.Name ?? "Default");

        // Set up a background task to auto-stop recording if the maximum length is exceeded
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(MaxTtsLength * 1000, token);
                if (_audioCapture.IsRecording)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (_audioCapture.IsRecording)
                        {
                            await _audioCapture.StopRecordingAsync();
                            _pcmChannel?.Writer.TryComplete();
                            _sound.PlayDoubleChirp();
                            MicStatus = "Finalizing (Limit reached)...";
                            StartTicking();
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
        });

        // Initialize the interaction record for this session
        var record = new InteractionRecord { ActionName = action.Name };
        _activeRecord = record;
        _sttLatencySw.Reset();
        
        // Start the background streaming task
        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for the configured pre-buffer delay before sending data
                await Task.Delay(PreBufferMs, token);
                if (IsGrokWebsocketStt)
                {
                    await StartGrokWebsocketStreaming(action, token, record);
                }
                else
                {
                    await StartGrokStreaming(action, token, record);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                record.ErrorMessage = ex.Message;
                record.UpdateDisplay();
                SafeAddConversationRecord(record);
            }
        });
    }

    /// <summary>
    /// Handles the release of the global hotkey.
    /// This finalizes the recording and triggers the post-processing phase.
    /// </summary>
    private async void OnHotKeyReleased(int actionId)
    {
        if (!_audioCapture.IsRecording) return;
        
        _sttLatencySw.Start();
        
        // Detection for accidental "short presses"
        var isShortPress = _captureCts != null && !_captureCts.IsCancellationRequested && Status.Contains("Buffering");
        if (isShortPress)
        {
            _sttLatencySw.Reset();
            _captureCts?.Cancel();
            _audioCapture.StopRecordingAsync().Wait();
            _sound.PlayError();
            Status = "Cancelled (Too short)";
            MicStatus = "Ready";
            await Task.Delay(1000);
            Status = "Ready";
            return;
        }

        // Finalize recording
        await _audioCapture.StopRecordingAsync();
        _pcmChannel?.Writer.TryComplete();
        _sound.PlayDoubleChirp();
        MicStatus = "Finalizing...";
        
        // Start "thinking" audio feedback
        StartTicking();
    }

    /// <summary>
    /// Manages the real-time streaming of audio data to the STT provider.
    /// </summary>
    private async Task StartGrokStreaming(ActionViewModel action, CancellationToken token, InteractionRecord record)
    {
        Status = $"Action: {action.Name} (Streaming...)";
        try
        {
            var provider = AiProviders.FirstOrDefault(p => p.Name == GrokProvider);
            if (provider == null || string.IsNullOrEmpty(provider.ApiKey))
            {
                _sound.PlayError();
                record.ErrorMessage = "Grok Provider not configured.";
                record.UpdateDisplay();
                SafeAddConversationRecord(record);
                StopTicking();
                return;
            }

            // Stream audio bytes from the channel to the AI service
            var sttResult = await _grok.StreamSpeechToTextAsync(_pcmChannel!.Reader, provider.ApiKey, GrokLanguage, SelectedCompression, token);
            _sttLatencySw.Stop();
            
            record.TtsDurationMs = _sttLatencySw.Elapsed.TotalMilliseconds;
            
            if (sttResult == null || string.IsNullOrEmpty(sttResult.Text) || sttResult.Text.StartsWith("Error"))
            {
                _sound.PlayError();
                record.ErrorMessage = $"STT Error: {sttResult?.Text ?? "Unknown"}";
                record.UpdateDisplay();
                SafeAddConversationRecord(record);
                StopTicking();
            }
            else
            {
                // STT Successful: Move to LLM post-processing
                record.RawStt = sttResult.Text;
                record.AudioDuration = sttResult.Duration;
                record.AudioFormat = sttResult.Format;
                record.RawAudioBytes = sttResult.RawBytes;
                record.BytesSent = sttResult.BytesSent;
                record.Compression = SelectedCompression.ToString();
                
                var sw = Stopwatch.StartNew();
                await ProcessActionResponse(sttResult.Text, action, record);
                sw.Stop();
                record.PostProcessingDurationMs = sw.Elapsed.TotalMilliseconds;
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _sound.PlayError();
                record.ErrorMessage = $"Streaming Error: {ex.Message}";
                record.UpdateDisplay();
                SafeAddConversationRecord(record);
            }
            StopTicking();
        }
        finally
        {
            // Reset UI state
            Dispatcher.UIThread.Post(() =>
            {
                MicStatus = "Ready";
                Status = "Ready";
            });
        }
    }

    /// <summary>
    /// Manages the real-time WebSocket streaming of audio data to the STT provider.
    /// </summary>
    private async Task StartGrokWebsocketStreaming(ActionViewModel action, CancellationToken token, InteractionRecord record)
    {
        Status = $"Action: {action.Name} (Streaming WebSocket...)";
        try
        {
            var provider = AiProviders.FirstOrDefault(p => p.Name == GrokProvider);
            if (provider == null || string.IsNullOrEmpty(provider.ApiKey))
            {
                _sound.PlayError();
                record.ErrorMessage = "Grok Provider not configured.";
                record.UpdateDisplay();
                SafeAddConversationRecord(record);
                StopTicking();
                return;
            }

            // Add the record to the UI early to display realtime updates
            SafeAddConversationRecord(record);

            var sb = new StringBuilder();
            string typedSoFar = "";
            
            // We can define a callback for when finalized text is received
            Action<string, bool> onTranscriptChunk = (chunkText, isFinal) =>
            {
                Console.Error.WriteLine($"[VM CALLBACK] chunkText='{chunkText}', isFinal={isFinal}");
                if (string.IsNullOrEmpty(chunkText)) return;
                
                Dispatcher.UIThread.Post(async () =>
                {
                    string targetText = "";
                    if (isFinal)
                    {
                        // Accumulate text
                        var currentAcc = sb.ToString();
                        var merged = GrokService.MergeOverlap(currentAcc, chunkText);
                        sb.Clear();
                        sb.Append(merged);
                        
                        targetText = sb.ToString();
                        record.RawStt = targetText;
                        
                        if (action.AiModel == "None")
                        {
                            record.TypedText = targetText;
                            record.UpdateDisplay();
                            
                            // Type the difference in realtime
                            int commonLen = 0;
                            while (commonLen < typedSoFar.Length && commonLen < targetText.Length && typedSoFar[commonLen] == targetText[commonLen])
                            {
                                commonLen++;
                            }
                            
                            int backspaces = typedSoFar.Length - commonLen;
                            string newSuffix = targetText.Substring(commonLen);
                            
                            string backspaceStr = new string('\b', backspaces);
                            string textToType = backspaceStr + newSuffix;
                            
                            Console.Error.WriteLine($"[TYPING] isFinal=true, typedSoFar='{typedSoFar}', targetText='{targetText}', commonLen={commonLen}, backspaces={backspaces}, typing='{textToType}'");
                            
                            typedSoFar = targetText;
                            if (!string.IsNullOrEmpty(textToType))
                            {
                                await _keyboard.TypeTextAsync(textToType);
                            }
                        }
                        else
                        {
                            record.UpdateDisplay();
                        }
                    }
                    else
                    {
                        // Interim results: show accumulated final text + current interim chunk on screen
                        targetText = GrokService.MergeOverlap(sb.ToString(), chunkText);
                        record.RawStt = targetText;
                        record.UpdateDisplay();
                        
                        if (action.AiModel == "None")
                        {
                            record.TypedText = targetText;
                            record.UpdateDisplay();
                            
                            // Type the difference in realtime
                            int commonLen = 0;
                            while (commonLen < typedSoFar.Length && commonLen < targetText.Length && typedSoFar[commonLen] == targetText[commonLen])
                            {
                                commonLen++;
                            }
                            
                            int backspaces = typedSoFar.Length - commonLen;
                            string newSuffix = targetText.Substring(commonLen);
                            
                            string backspaceStr = new string('\b', backspaces);
                            string textToType = backspaceStr + newSuffix;
                            
                            Console.Error.WriteLine($"[TYPING] isFinal=false, typedSoFar='{typedSoFar}', targetText='{targetText}', commonLen={commonLen}, backspaces={backspaces}, typing='{textToType}'");
                            
                            typedSoFar = targetText;
                            if (!string.IsNullOrEmpty(textToType))
                            {
                                await _keyboard.TypeTextAsync(textToType);
                            }
                        }
                    }
                });
            };

            var sttResult = await _grok.StreamSpeechToTextWebsocketAsync(_pcmChannel!.Reader, provider.ApiKey, GrokLanguage, onTranscriptChunk, token);
            _sttLatencySw.Stop();
            
            record.TtsDurationMs = _sttLatencySw.Elapsed.TotalMilliseconds;
            
            if (sttResult == null || string.IsNullOrEmpty(sttResult.Text) || sttResult.Text.StartsWith("Error"))
            {
                // Only play error if it was not cancelled by user
                if (!token.IsCancellationRequested)
                {
                    _sound.PlayError();
                    record.ErrorMessage = $"STT WebSocket Error: {sttResult?.Text ?? "Unknown"}";
                    record.UpdateDisplay();
                }
                StopTicking();
            }
            else
            {
                record.RawStt = sttResult.Text;
                record.AudioDuration = sttResult.Duration;
                record.AudioFormat = sttResult.Format;
                record.RawAudioBytes = sttResult.RawBytes;
                record.BytesSent = sttResult.BytesSent;
                record.Compression = "None"; // WebSockets sends raw PCM

                var sw = Stopwatch.StartNew();
                await ProcessActionResponse(sttResult.Text, action, record);
                sw.Stop();
                record.PostProcessingDurationMs = sw.Elapsed.TotalMilliseconds;
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                _sound.PlayError();
                record.ErrorMessage = $"WebSocket Streaming Error: {ex.Message}";
                record.UpdateDisplay();
            }
            StopTicking();
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                MicStatus = "Ready";
                Status = "Ready";
            });
        }
    }

    /// <summary>
    /// Processes the transcribed text through an LLM based on the specified action.
    /// This is the core post-processing pipeline of the application.
    /// </summary>
    private async Task ProcessActionResponse(string text, ActionViewModel action, InteractionRecord record)
    {
        try
        {
            // If the action is configured to use an AI model (not "None")
            if (action.AiModel != "None")
            {
                var isAppendMode = action.AiModel == "Append To Last Reply";
                
                // Determine which model to use: 
                // 1. If append mode, use the last successful model
                // 2. If action has a specific model (not Default), use that
                // 3. Otherwise use the system default model
                var modelName = isAppendMode ? _lastSuccessfulModel : ((string.IsNullOrEmpty(action.AiModel) || action.AiModel == "Default") ? Llms.FirstOrDefault(l => l.IsDefault)?.Model : action.AiModel);
                var providerName = isAppendMode ? _lastSuccessfulProvider : null;

                // Validation for append mode
                if (isAppendMode && (string.IsNullOrEmpty(_lastSuccessfulPrompt) || string.IsNullOrEmpty(modelName)))
                {
                    _sound.PlayError();
                    record.ErrorMessage = "No previous LLM action to append to.";
                    record.UpdateDisplay();
                    SafeAddConversationRecord(record);
                    return;
                }

                // Find the model configuration
                var model = Llms.FirstOrDefault(l => l.Model == modelName) ?? Llms.FirstOrDefault(l => l.IsDefault);
                if (model != null)
                {
                    // Mark the record with its append mode status for future reference
                    record.IsAppendMode = isAppendMode;
                    
                    // Find the provider for this model
                    var llmProvider = AiProviders.FirstOrDefault(p => p.Name == (providerName ?? model.ProviderName));
                    if (llmProvider != null && !string.IsNullOrEmpty(llmProvider.ApiKey))
                    {
                        // Build the message list for the LLM request
                        var systemPrompt = isAppendMode ? _lastSuccessfulPrompt! : action.Prompt;
                        var messages = new List<ChatMessage> { new ChatMessage { role = "system", content = systemPrompt } };

                        // If appending, find the conversation "root" (the last fresh question) and include all context from there
                        if (isAppendMode)
                        {
                            // 1. Get all valid interactions (those with STT and AI response)
                            var validHistory = Conversation
                                .Where(r => !r.IsSystemMessage && !string.IsNullOrEmpty(r.RawStt) && !string.IsNullOrEmpty(r.LlmMarkdown))
                                .ToList();

                            // 2. Find the index of the most recent "fresh" (non-append) interaction
                            int rootIndex = -1;
                            for (int i = validHistory.Count - 1; i >= 0; i--)
                            {
                                if (!validHistory[i].IsAppendMode)
                                {
                                    rootIndex = i;
                                    break;
                                }
                            }

                            // 3. If we found a root, take everything from that point forward as context
                            if (rootIndex != -1)
                            {
                                var contextRecords = validHistory.Skip(rootIndex).ToList();
                                foreach (var prev in contextRecords)
                                {
                                    messages.Add(new ChatMessage { role = "user", content = prev.RawStt! });
                                    messages.Add(new ChatMessage { role = "assistant", content = prev.LlmMarkdown! });
                                }
                            }
                        }

                        // Add the current transcribed text as the final user message
                        messages.Add(new ChatMessage { role = "user", content = text });
                        
                        // Execute the LLM request
                        var result = await _grok.ProcessActionAsync(messages, llmProvider.ApiKey, llmProvider.HostUrl, model.Model);

                        if (result != null)
                        {
                            // Store the raw request/response for debugging
                            record.LlmRequest = result.LlmRequest;
                            record.LlmResponse = result.FullResponse;
                            record.LlmModel = model.Model;

                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                _sound.PlayError();
                                record.ErrorMessage = result.Error;
                            }
                            else
                            {
                                // Success! Store this as the "last successful" state for future append actions
                                if (!isAppendMode)
                                {
                                    _lastSuccessfulPrompt = action.Prompt;
                                    _lastSuccessfulModel = model.Model;
                                    _lastSuccessfulProvider = llmProvider.Name;
                                }

                                record.LlmMarkdown = result.Markdown;
                                record.TypedText = result.Keyboard;
                                record.UpdateDisplay();
                                SafeAddConversationRecord(record);

                                // Handle UI feedback: activate window if requested
                                if (action.ShowPopup && !string.IsNullOrEmpty(result.Markdown))
                                {
                                    SelectedTabIndex = 0;
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        var owner = GetMainWindow();
                                        if (owner != null)
                                        {
                                            owner.Activate();
                                            if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
                                        }
                                    });
                                }

                                // Handle TTS feedback
                                if (action.UseTts && !string.IsNullOrEmpty(result.Markdown))
                                {
                                    await Task.Delay(100);
                                    var ttsSw = Stopwatch.StartNew();
                                    await SpeakTtsAsync(result.Markdown, result.Keyboard);
                                    ttsSw.Stop();
                                    record.SpeechGenDurationMs = ttsSw.Elapsed.TotalMilliseconds;
                                }

                                // Handle keyboard injection
                                if (!string.IsNullOrEmpty(result.Keyboard))
                                {
                                    await _keyboard.TypeTextAsync(result.Keyboard);
                                }
                                return;
                            }
                        }
                        else
                        {
                            record.ErrorMessage = "LLM returned no result.";
                        }
                    }
                    else
                    {
                        record.ErrorMessage = "LLM Provider not configured.";
                    }
                }
                else
                {
                    record.ErrorMessage = "No LLM Model found.";
                }
            }
            else
            {
                // No AI model selected: Just type the raw transcribed text
                record.TypedText = text;
                if (!IsGrokWebsocketStt)
                {
                    await _keyboard.TypeTextAsync(text);
                }
            }

            // Finalize the record display
            record.UpdateDisplay();
            SafeAddConversationRecord(record);
        }
        finally
        {
            // Always stop the "thinking" ticking sound when processing finishes
            StopTicking();
        }
    }

    /// <summary>
    /// Safely adds a record to the conversation on the UI thread if not already present.
    /// </summary>
    private void SafeAddConversationRecord(InteractionRecord record)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!Conversation.Contains(record))
            {
                Conversation.Add(record);
            }
        });
    }

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
        if (await dialog.ShowDialog<bool>(targetOwner))
        {
            AiProviders.Add(newProvider);
            SaveLocalData();
        }
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
        var oldIsGrok = IsGrokStt;
        var oldIsGrokWs = IsGrokWebsocketStt;
        var oldIsVox = IsVoxStt;
        var oldProvider = GrokProvider;
        var oldLang = GrokLanguage;
        var oldComp = SelectedCompression;
        EditingGrokStt = true;
        EditingVoxStt = false;
        var dialog = new SttConfigDialog(this);
        dialog.DataContext = dialog;
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            if (await dialog.ShowDialog<bool>(mainWindow)) SaveLocalData();
            else
            {
                _isGrokStt = oldIsGrok;
                _isGrokWebsocketStt = oldIsGrokWs;
                _isVoxStt = oldIsVox;
                this.RaisePropertyChanged(nameof(IsGrokStt));
                this.RaisePropertyChanged(nameof(IsGrokWebsocketStt));
                this.RaisePropertyChanged(nameof(IsVoxStt));
                GrokProvider = oldProvider;
                GrokLanguage = oldLang;
                SelectedCompression = oldComp;
            }
        }
    }

    public async Task EditVoxStt()
    {
        var oldIsGrok = IsGrokStt;
        var oldIsGrokWs = IsGrokWebsocketStt;
        var oldIsVox = IsVoxStt;
        var oldUrl = VoxAssistHostUrl;
        EditingGrokStt = false;
        EditingVoxStt = true;
        var dialog = new SttConfigDialog(this);
        dialog.DataContext = dialog;
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            if (await dialog.ShowDialog<bool>(mainWindow)) SaveLocalData();
            else
            {
                _isGrokStt = oldIsGrok;
                _isGrokWebsocketStt = oldIsGrokWs;
                _isVoxStt = oldIsVox;
                this.RaisePropertyChanged(nameof(IsGrokStt));
                this.RaisePropertyChanged(nameof(IsGrokWebsocketStt));
                this.RaisePropertyChanged(nameof(IsVoxStt));
                VoxAssistHostUrl = oldUrl;
            }
        }
    }

    private async Task EditLlm(LlmViewModel llm, bool isNew = false)
    {
        var dialog = new LlmEditDialog(this) { DataContext = llm, IsNew = isNew };
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;
        if (await dialog.ShowDialog<bool>(mainWindow))
        {
            if (dialog.IsDeleted)
            {
                if (!isNew) Llms.Remove(llm);
            }
            else
            {
                if (llm.IsDefault)
                {
                    foreach (var l in Llms)
                    {
                        if (l != llm) l.IsDefault = false;
                    }
                }
                if (isNew) Llms.Add(llm);
                foreach (var l in Llms) l.RaisePropertyChanged(nameof(l.DisplayName));
            }
            UpdateLlmSelector();
            SaveLocalData();
        }
    }

    private Window? GetMainWindow() => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private void UpdateLlmSelector()
    {
        LlmSelectorItems.Clear();
        LlmSelectorItems.Add(new LlmViewModel { Model = "Default", ProviderName = "" });
        LlmSelectorItems.Add(new LlmViewModel { Model = "Append To Last Reply", ProviderName = "" });
        LlmSelectorItems.Add(new LlmViewModel { Model = "None", ProviderName = "" });
        foreach (var llm in Llms) LlmSelectorItems.Add(llm);
        foreach (var action in Actions)
        {
            if (!string.IsNullOrEmpty(action.AiModel) && action.AiModel != "None" && action.AiModel != "Append To Last Reply" && !Llms.Any(l => l.Model == action.AiModel))
            {
                action.AiModel = "";
            }
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

    private async Task SpeakTtsAsync(string markdown, string? keyboard)
    {
        var textToSpeak = string.IsNullOrWhiteSpace(markdown) ? keyboard : markdown;
        if (string.IsNullOrWhiteSpace(textToSpeak)) return;
        try
        {
            var provider = AiProviders.FirstOrDefault(p => p.Name == GrokProvider);
            if (provider != null && !string.IsNullOrEmpty(provider.ApiKey))
            {
                var audioData = await _grokTts.GenerateSpeechAsync(textToSpeak, provider.ApiKey, GrokTtsVoice, GrokLanguage);
                if (audioData != null)
                {
                    await _sound.PlayAudioAsync(audioData);
                    return;
                }
            }
        }
        catch { }
        await SpeakAsync(textToSpeak!);
    }

    private async Task SpeakAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var sanitized = text.Replace("\"", "\\\"").Replace("'", "");
            if (OperatingSystem.IsLinux())
            {
                var spdSayPath = "/usr/bin/spd-say";
                if (!File.Exists(spdSayPath)) spdSayPath = "spd-say";
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = spdSayPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add(text);
                process.Start();
                await process.WaitForExitAsync();
            }
            else if (OperatingSystem.IsWindows())
            {
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
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _hotkey.Dispose();
        _respeaker.Dispose();
        _keyboard.Dispose();
    }
}
