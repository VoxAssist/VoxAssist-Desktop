using System;
using ReactiveUI;
using VoxAssist.Desktop.ViewModels;

namespace VoxAssist.Desktop.Models;

public class InteractionRecord : ViewModelBase
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? ActionName { get; set; }
    public string? RawStt { get; set; }
    public double AudioDuration { get; set; }
    public string? AudioFormat { get; set; }
    public long RawAudioBytes { get; set; }
    public long BytesSent { get; set; }
    public string? Compression { get; set; }
    public double TtsDurationMs { get; set; }
    public double SpeechGenDurationMs { get; set; }
    public double PostProcessingDurationMs { get; set; }
    public string? LlmModel { get; set; }
    
    public double CompressionSavings => RawAudioBytes > 0 
        ? Math.Max(0, (1.0 - (double)BytesSent / RawAudioBytes) * 100.0) 
        : 0;

    public string? LlmRequest { get; set; }
    public string? LlmResponse { get; set; }
    public string? LlmMarkdown { get; set; }
    public string? TypedText { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSystemMessage { get; set; }
    public string? SystemText { get; set; }

    private string _displayMarkdown = "";
    public string DisplayMarkdown 
    { 
        get => _displayMarkdown; 
        set => this.RaiseAndSetIfChanged(ref _displayMarkdown, value); 
    }

    public void UpdateDisplay()
    {
        if (IsSystemMessage)
        {
            DisplayMarkdown = $"System: {SystemText}";
            return;
        }

        var audioInfo = (AudioDuration > 0) ? $"\n*[Duration: {AudioDuration:F1}s ({AudioFormat ?? "PCM"})]*" : "";

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            var prompt = string.IsNullOrEmpty(RawStt) ? "" : $"**You:**{audioInfo}\n{RawStt}\n\n";
            DisplayMarkdown = $"{prompt}**Error:** {ErrorMessage}";
            return;
        }

        if (!string.IsNullOrEmpty(LlmMarkdown))
        {
            DisplayMarkdown = $"**You:**{audioInfo}\n{RawStt}\n\n**AI:** {LlmMarkdown}";
            return;
        }

        if (!string.IsNullOrEmpty(TypedText))
        {
            // Omit "You:" for standalone dictation/typing
            DisplayMarkdown = $"**Typed:** {TypedText}";
            return;
        }

        if (!string.IsNullOrEmpty(RawStt))
        {
            DisplayMarkdown = $"**You:**{audioInfo}\n{RawStt}";
        }
    }

    public override string ToString() => DisplayMarkdown;
}
