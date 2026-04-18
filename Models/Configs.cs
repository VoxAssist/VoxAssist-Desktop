using System;

namespace VoxAssist.Desktop.Models;

public class LlmConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string HostUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool IsDefault { get; set; }
}

public class ActionConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public Guid LlmId { get; set; }
}

public enum CompressionType
{
    None,
    G711,
    Flac
}
