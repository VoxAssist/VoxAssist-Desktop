using System;
using ReactiveUI;
using VoxAssist.Desktop.ViewModels;

namespace VoxAssist.Desktop.Models;

public class InteractionRecord : ViewModelBase
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? ActionName { get; set; }
    public string? RawStt { get; set; }
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

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            var prompt = string.IsNullOrEmpty(RawStt) ? "" : $"**You:** {RawStt}\n\n";
            DisplayMarkdown = $"{prompt}**Error:** {ErrorMessage}";
            return;
        }

        if (!string.IsNullOrEmpty(LlmMarkdown))
        {
            DisplayMarkdown = $"**You:** {RawStt}\n\n**AI:** {LlmMarkdown}";
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
            DisplayMarkdown = $"**You:** {RawStt}";
        }
    }

    public override string ToString() => DisplayMarkdown;
}
