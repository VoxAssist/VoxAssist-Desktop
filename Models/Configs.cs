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

public class UserConfig
{
    public bool IsCcw { get; set; }
    public string GrokProvider { get; set; } = "";
    public string GrokLanguage { get; set; } = "en";
    public string GrokTtsVoice { get; set; } = "eve";
    public int MaxTtsLength { get; set; } = 600;
    public string SttModel { get; set; } = GrokSttOptions.Model20;
    public bool? SttInterimResults { get; set; }
    public int? SttEndpointingMs { get; set; }
    public bool SttDiarize { get; set; }
    public bool SttFillerWords { get; set; }
    public string SttKeyTerms { get; set; } = "";
    public bool SttSmartTurnEnabled { get; set; }
    public double? SttSmartTurnThreshold { get; set; }
    public int? SttSmartTurnTimeoutMs { get; set; }
    public double? SttVadThreshold { get; set; }
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;
}
