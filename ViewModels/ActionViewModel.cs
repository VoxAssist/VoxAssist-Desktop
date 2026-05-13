using ReactiveUI;
using SharpHook;
using SharpHook.Native;
using SharpHook.Data;
using System.Collections.Generic;
using System.Linq;

namespace VoxAssist.Desktop.ViewModels;

public class ActionViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set { this.RaiseAndSetIfChanged(ref _name, value); } }

    private string _prompt = "";
    public string Prompt { get => _prompt; set { this.RaiseAndSetIfChanged(ref _prompt, value); } }

    private string _hotkeyDisplay = "None";
    public string HotkeyDisplay { get => _hotkeyDisplay; set => this.RaiseAndSetIfChanged(ref _hotkeyDisplay, value); }

    private string _aiModel = "";
    public string AiModel { get => _aiModel; set { this.RaiseAndSetIfChanged(ref _aiModel, value); } }

    private bool _showPopup;
    public bool ShowPopup { get => _showPopup; set { this.RaiseAndSetIfChanged(ref _showPopup, value); } }

    private bool _useTts;
    public bool UseTts { get => _useTts; set { this.RaiseAndSetIfChanged(ref _useTts, value); } }

    public List<KeyCode> Hotkey { get; set; } = new();

    public ActionViewModel Clone()
    {
        return new ActionViewModel
        {
            Name = this.Name,
            Prompt = this.Prompt,
            HotkeyDisplay = this.HotkeyDisplay,
            AiModel = this.AiModel,
            ShowPopup = this.ShowPopup,
            UseTts = this.UseTts,
            Hotkey = new List<KeyCode>(this.Hotkey)
        };
    }

    public bool IsEqualTo(ActionViewModel other)
    {
        if (other == null) return false;
        return Name == other.Name &&
               Prompt == other.Prompt &&
               AiModel == other.AiModel &&
               ShowPopup == other.ShowPopup &&
               UseTts == other.UseTts &&
               Hotkey.SequenceEqual(other.Hotkey);
    }
}
