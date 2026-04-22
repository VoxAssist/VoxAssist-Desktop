using System;
using System.Collections.Generic;

namespace VoxAssist.Desktop.Models;

public class AiProviderConfig
{
    public string Name { get; set; } = "";
    public string HostUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class LlmConfig
{
    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public bool IsDefault { get; set; }
}

public class ActionConfig
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Hotkey { get; set; } = "";
    
    // Use readable matching instead of Guid
    public string AiModel { get; set; } = "";
    public bool ShowPopup { get; set; }
    public bool UseTts { get; set; }
}

public enum CompressionType
{
    None,
    G711,
    Flac
}

public class UserConfig
{
    public bool IsCcw { get; set; }
    public bool IsGrokStt { get; set; } = true;
    public string GrokProvider { get; set; } = "";
    public string GrokLanguage { get; set; } = "en";
    public string VoxAssistHostUrl { get; set; } = "";
}
