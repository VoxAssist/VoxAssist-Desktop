using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using VoxAssist.Desktop.Views;
using Avalonia.Controls;
using SharpHook.Native;
using SharpHook.Data;
using VoxAssist.Desktop.Models;

namespace VoxAssist.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private ActionViewModel? _selectedAction;
    private ActionViewModel? _originalAction;
    private IDisposable? _selectedActionSubscription;

    public ActionViewModel? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (_selectedAction == value) return;

            if (CanSaveSelectedAction)
            {
                _ = HandleActionSelectionChange(value);
            }
            else
            {
                UpdateSelectedAction(value);
            }
        }
    }

    private async Task HandleActionSelectionChange(ActionViewModel? newAction)
    {
        var result = await CheckUnsavedChangesAsync();
        if (result == "Cancel")
        {
            this.RaisePropertyChanged(nameof(SelectedAction));
            return;
        }

        if (result == "Save") SaveSelectedAction();
        else if (result == "Discard") CancelSelectedAction();

        UpdateSelectedAction(newAction);
    }

    /// <summary>
    /// Updates the currently selected action and sets up a change listener 
    /// to update the "CanSave" status.
    /// </summary>
    private void UpdateSelectedAction(ActionViewModel? action)
    {
        _selectedActionSubscription?.Dispose();
        this.RaiseAndSetIfChanged(ref _selectedAction, action, nameof(SelectedAction));
        _originalAction = action?.Clone();

        if (_selectedAction != null)
        {
            // Subscribe to changes in the action model to update UI buttons (Save/Cancel)
            _selectedActionSubscription = _selectedAction.Changed.Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(CanSaveSelectedAction));
                this.RaisePropertyChanged(nameof(SelectedActionLlm));
                this.RaisePropertyChanged(nameof(IsPromptEnabled));
            });
        }

        // Trigger UI updates for dependent properties
        this.RaisePropertyChanged(nameof(SelectedActionLlm));
        this.RaisePropertyChanged(nameof(IsPromptEnabled));
        this.RaisePropertyChanged(nameof(CanSaveSelectedAction));
    }

    /// <summary>
    /// UI-bound property that determines if the Save/Cancel buttons should be enabled.
    /// </summary>
    public bool CanSaveSelectedAction
    {
        get
        {
            if (SelectedAction == null || _originalAction == null) return false;
            // Compares current values against the state when the action was selected
            return !SelectedAction.IsEqualTo(_originalAction);
        }
    }

    /// <summary>
    /// Helper property to map between the "AiModel" string and the LLM selector items.
    /// </summary>
    public LlmViewModel? SelectedActionLlm
    {
        get => LlmSelectorItems.FirstOrDefault(l => l.Model == SelectedAction?.AiModel || (l.Model == "Default" && string.IsNullOrEmpty(SelectedAction?.AiModel)));
        set
        {
            if (SelectedAction != null && value != null)
            {
                SelectedAction.AiModel = value.Model == "Default" ? "" : value.Model;

                // Certain modes like "Append" or "None" don't support custom system prompts
                if (SelectedAction.AiModel == "None" || SelectedAction.AiModel == "Append To Last Reply")
                {
                    SelectedAction.Prompt = "";
                }

                this.RaisePropertyChanged(nameof(SelectedActionLlm));
                this.RaisePropertyChanged(nameof(IsPromptEnabled));
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

    /// <summary>
    /// Shows a dialog to record a new global hotkey for the selected action.
    /// </summary>
    public async Task SetActionHotkey()
    {
        if (SelectedAction == null) return;
        
        // Open the specialized hotkey recording window
        var window = new HotkeyWindow(_hotkey, SelectedAction.Name);
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var result = await window.ShowDialog<List<KeyCode>>(mainWindow);
        if (result != null && result.Count > 0)
        {
            // Update the model and UI display string
            SelectedAction.Hotkey = result;
            SelectedAction.HotkeyDisplay = string.Join(" + ", result.Select(k => k.ToString().Replace("Vc", "")));
            
            // Re-initialize the hotkey listener with the new mapping
            UpdateHotkeyService();
            SaveLocalData();
        }
    }

    public void AddAction()
    {
        if (CanSaveSelectedAction) _ = HandleAddActionAsync();
        else PerformAddAction();
    }

    private async Task HandleAddActionAsync()
    {
        var result = await CheckUnsavedChangesAsync();
        if (result == "Cancel") return;
        if (result == "Save") SaveSelectedAction();
        else if (result == "Discard") CancelSelectedAction();
        PerformAddAction();
    }

    private void PerformAddAction()
    {
        var newAction = new ActionViewModel
        {
            Name = "New Action",
            Prompt = "You are a helpfull voice assistant",
            AiModel = "",
            ShowPopup = false,
            UseTts = false
        };
        Actions.Add(newAction);
        UpdateSelectedAction(newAction);
        SaveLocalData();
    }

    /// <summary>
    /// Cancels changes made to the currently selected action by reverting to the original state.
    /// </summary>
    public void CancelSelectedAction()
    {
        if (SelectedAction == null || _originalAction == null) return;

        SelectedAction.Name = _originalAction.Name;
        SelectedAction.Prompt = _originalAction.Prompt;
        SelectedAction.AiModel = _originalAction.AiModel;
        SelectedAction.ShowPopup = _originalAction.ShowPopup;
        SelectedAction.UseTts = _originalAction.UseTts;
        SelectedAction.Hotkey = new List<KeyCode>(_originalAction.Hotkey);
        SelectedAction.HotkeyDisplay = _originalAction.HotkeyDisplay;

        this.RaisePropertyChanged(nameof(SelectedActionLlm));
        this.RaisePropertyChanged(nameof(IsPromptEnabled));
        this.RaisePropertyChanged(nameof(CanSaveSelectedAction));
    }

    public void SaveSelectedAction()
    {
        SaveLocalData();
        _originalAction = SelectedAction?.Clone();
        this.RaisePropertyChanged(nameof(CanSaveSelectedAction));
    }

    public void DeleteSelectedAction()
    {
        if (SelectedAction != null)
        {
            Actions.Remove(SelectedAction);
            SaveLocalData();
        }
    }
}
