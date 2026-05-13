using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ReactiveUI;
using VoxAssist.Desktop.Models;
using VoxAssist.Desktop.Services;
using VoxAssist.Desktop.Views;
using SharpHook.Native;
using SharpHook.Data;

namespace VoxAssist.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// Gets the platform-specific directory for storing application settings and data.
    /// </summary>
    private string GetSettingsDir()
    {
        string path;
        if (OperatingSystem.IsMacOS())
        {
            // macOS standard: ~/Library/Application Support/VoxAssist
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VoxAssist");
        }
        else
        {
            // Linux/Windows standard: ~/.config/VoxAssist or %AppData%/VoxAssist
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxAssist");
        }

        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }

    private void MigrateSettingsIfNecessary(string targetDir)
    {
        try
        {
            var localSettingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings");
            if (Directory.Exists(localSettingsDir) && Path.GetFullPath(localSettingsDir) != Path.GetFullPath(targetDir))
            {
                string[] files = { "settings.json", "ai_models.json", "actions.json", "ai_providers.json" };
                foreach (var file in files)
                {
                    var sourceFile = Path.Combine(localSettingsDir, file);
                    var targetFile = Path.Combine(targetDir, file);
                    if (File.Exists(sourceFile) && !File.Exists(targetFile))
                    {
                        File.Copy(sourceFile, targetFile);
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Loads all application data from JSON files in the settings directory.
    /// Also initializes default values if files are missing.
    /// </summary>
    private void LoadLocalData()
    {
        try
        {
            var settingsDir = GetSettingsDir();
            MigrateSettingsIfNecessary(settingsDir);

            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName().Name;
            string[] files = { "settings.json", "ai_models.json", "actions.json", "ai_providers.json" };

            // Ensure all configuration files exist by copying defaults from embedded resources if necessary
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

            // 1. Load AI Providers (xAI, OpenAI, etc.)
            var providersPath = Path.Combine(settingsDir, "ai_providers.json");
            if (File.Exists(providersPath))
            {
                var json = File.ReadAllText(providersPath);
                var providers = System.Text.Json.JsonSerializer.Deserialize<List<AiProviderConfig>>(json);
                if (providers != null)
                {
                    AiProviders.Clear();
                    foreach (var p in providers)
                    {
                        AiProviders.Add(new AiProviderViewModel
                        {
                            Name = p.Name,
                            HostUrl = p.HostUrl,
                            ApiKey = p.ApiKey
                        });
                    }
                }
            }

            // 2. Load LLM Models (grok-2, gpt-4, etc.)
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
                        Llms.Add(new LlmViewModel
                        {
                            ProviderName = l.ProviderName,
                            Model = l.Model,
                            IsDefault = l.IsDefault
                        });
                    }
                }
            }

            // 3. Load User Actions (Dictation, AI Assistant, etc.)
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
                        var hotkey = string.IsNullOrEmpty(a.Hotkey)
                            ? new List<KeyCode>()
                            : a.Hotkey.Split(',').Select(s => (KeyCode)int.Parse(s)).ToList();

                        var hotkeyDisplay = hotkey.Count > 0
                            ? string.Join(" + ", hotkey.Select(k => k.ToString().Replace("Vc", "")))
                            : "None";

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

            // 4. Load General Application Settings
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
                    GrokTtsVoice = string.IsNullOrEmpty(config.GrokTtsVoice) ? "eve" : config.GrokTtsVoice;
                    VoxAssistHostUrl = config.VoxAssistHostUrl;
                    MaxTtsLength = config.MaxTtsLength > 0 ? config.MaxTtsLength : 600;
                    SelectedCompression = config.SelectedCompression;
                    _lastUpdateCheck = config.LastUpdateCheck;
                }
            }
        }
        catch { }

        // Ensure we have at least one valid provider/model/action if everything failed to load
        if (AiProviders.Count == 0) AiProviders.Add(new AiProviderViewModel { Name = "xAI", HostUrl = "https://api.x.ai/v1" });
        if (Llms.Count == 0) Llms.Add(new LlmViewModel { ProviderName = "xAI", Model = "grok-2", IsDefault = true });
        if (Actions.Count == 0)
        {
            Actions.Add(new ActionViewModel
            {
                Name = "Dictation",
                Prompt = "You are a voice assistant, taking dictation...",
                AiModel = ""
            });
        }

        // Initialize dependent services
        UpdateLlmSelector();
        UpdateHotkeyService();

        // Add a "Ready" message to the interaction history
        var readyRecord = new InteractionRecord { IsSystemMessage = true, SystemText = "Ready" };
        readyRecord.UpdateDisplay();
        Conversation.Add(readyRecord);
    }

    /// <summary>
    /// Saves all current application state to the JSON configuration files.
    /// </summary>
    private void SaveLocalData()
    {
        try
        {
            var settingsDir = GetSettingsDir();
            if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);

            // 1. Save general settings
            var config = new UserConfig
            {
                IsCcw = IsCcw,
                IsGrokStt = IsGrokStt,
                GrokProvider = GrokProvider,
                GrokLanguage = GrokLanguage,
                GrokTtsVoice = GrokTtsVoice,
                VoxAssistHostUrl = VoxAssistHostUrl,
                MaxTtsLength = MaxTtsLength,
                SelectedCompression = SelectedCompression,
                LastUpdateCheck = _lastUpdateCheck
            };
            File.WriteAllText(Path.Combine(settingsDir, "settings.json"), System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // 2. Save AI Providers
            var providers = AiProviders.Select(p => new AiProviderConfig
            {
                Name = p.Name,
                HostUrl = p.HostUrl,
                ApiKey = p.ApiKey
            }).ToList();
            File.WriteAllText(Path.Combine(settingsDir, "ai_providers.json"), System.Text.Json.JsonSerializer.Serialize(providers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // 3. Save User Actions
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

            // 4. Save LLM Models
            var models = Llms.Select(l => new LlmConfig
            {
                ProviderName = l.ProviderName,
                Model = l.Model,
                IsDefault = l.IsDefault
            }).ToList();
            File.WriteAllText(Path.Combine(settingsDir, "ai_models.json"), System.Text.Json.JsonSerializer.Serialize(models, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void CheckForUpdatesOnStart() => _ = Task.Run(async () =>
    {
        await Task.Delay(2000);
        // Daily check logic
        if (DateTime.Now - _lastUpdateCheck > TimeSpan.FromDays(1))
        {
            AvailableUpdate = await _updater.CheckForUpdatesAsync(_version);
            _lastUpdateCheck = DateTime.Now;
            SaveLocalData();
        }
    });

    public async Task ManualCheckForUpdates()
    {
        Status = "Checking for updates...";
        AvailableUpdate = await _updater.CheckForUpdatesAsync(_version);
        _lastUpdateCheck = DateTime.Now;
        SaveLocalData();

        Status = AvailableUpdate == null ? "App is up to date." : $"Update {AvailableUpdate.Version} available!";
        if (AvailableUpdate == null) await Task.Delay(3000);
        Status = "Ready";
    }

    public async Task PerformUpdate()
    {
        if (AvailableUpdate == null) return;
        try
        {
            Status = $"Downloading update {AvailableUpdate.Version}...";
            await _updater.ApplyUpdateAsync(AvailableUpdate, (p) => UpdateProgress = p);
        }
        catch (Exception ex)
        {
            Status = $"Update failed: {ex.Message}";
            await Task.Delay(5000);
            Status = "Ready";
        }
    }
}
