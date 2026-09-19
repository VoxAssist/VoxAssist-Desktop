using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VoxAssist.Desktop.Models;

public class GrokSttOptions
{
    public const string Model20 = "grok-voice-transcribe-2.0";
    public const string Model10 = "grok-voice-transcribe-1.0";
    public const string AutoLanguage = "auto";

    public string Model { get; set; } = Model20;
    public string Language { get; set; } = "en";
    public bool InterimResults { get; set; } = true;
    public int EndpointingMs { get; set; } = 400;
    public bool Diarize { get; set; }
    public bool FillerWords { get; set; }
    public List<string> KeyTerms { get; set; } = new();
    public bool SmartTurnEnabled { get; set; }
    public double SmartTurnThreshold { get; set; } = 0.7;
    public int SmartTurnTimeoutMs { get; set; } = 3000;
    public double VadThreshold { get; set; } = 0.08;

    public static IReadOnlyList<string> ParseKeyTerms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        return raw
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 50 ? t[..50] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }

    public string BuildWebSocketUrl()
    {
        var qs = new StringBuilder();

        void Add(string key, string value)
        {
            qs.Append(qs.Length == 0 ? '?' : '&');
            qs.Append(Uri.EscapeDataString(key));
            qs.Append('=');
            qs.Append(Uri.EscapeDataString(value));
        }

        var model = string.Equals(Model, Model10, StringComparison.Ordinal) ? Model10 : Model20;
        Add("model", model);
        Add("sample_rate", "16000");
        Add("encoding", "pcm");
        Add("interim_results", InterimResults ? "true" : "false");
        Add("endpointing", Math.Clamp(EndpointingMs, 0, 5000).ToString(CultureInfo.InvariantCulture));
        Add("diarize", Diarize ? "true" : "false");
        Add("filler_words", FillerWords ? "true" : "false");
        Add("vad_threshold", Math.Clamp(VadThreshold, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(Language) &&
            !string.Equals(Language, AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            Add("language", Language.Trim());
        }

        if (SmartTurnEnabled)
        {
            Add("smart_turn", Math.Clamp(SmartTurnThreshold, 0.0, 1.0).ToString("0.###", CultureInfo.InvariantCulture));
            Add("smart_turn_timeout", Math.Clamp(SmartTurnTimeoutMs, 1, 5000).ToString(CultureInfo.InvariantCulture));
        }

        foreach (var term in KeyTerms
                     .Select(t => t.Trim())
                     .Where(t => t.Length > 0)
                     .Take(100))
        {
            Add("keyterm", term.Length > 50 ? term[..50] : term);
        }

        return "wss://api.x.ai/v1/stt" + qs;
    }
}
