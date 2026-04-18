using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Native;
using SharpHook.Data;

namespace VoxAssist.Desktop.Services;

public class HotkeyService : IDisposable
{
    private readonly TaskPoolGlobalHook _hook;
    private readonly HashSet<KeyCode> _pressedKeys = new();
    
    // Mapping ActionId -> Sequence
    private Dictionary<int, List<KeyCode>> _actionHotkeys = new();

    public event Action<int>? HotKeyPressedDynamic;
    public event Action<int>? HotKeyReleasedDynamic;
    public event Action<List<KeyCode>>? HotKeyRecorded;

    private int? _activeActionId = null;
    public bool IsRecordingMode { get; set; } = false;

    public HotkeyService()
    {
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    public void Start() => Task.Run(() => _hook.Run());

    public void UpdateHotkeys(Dictionary<int, List<KeyCode>> mapping)
    {
        _actionHotkeys = mapping;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        _pressedKeys.Add(e.Data.KeyCode);
        if (IsRecordingMode) return;
        CheckHotKey();
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (IsRecordingMode)
        {
            var captured = _pressedKeys.ToList();
            if (captured.Count > 0)
            {
                HotKeyRecorded?.Invoke(captured);
                IsRecordingMode = false;
            }
        }
        
        CheckHotKey();
        _pressedKeys.Remove(e.Data.KeyCode);
    }

    private void CheckHotKey()
    {
        if (_activeActionId == null)
        {
            foreach (var pair in _actionHotkeys)
            {
                if (IsSequencePressed(pair.Value))
                {
                    _activeActionId = pair.Key;
                    HotKeyPressedDynamic?.Invoke(pair.Key);
                    break;
                }
            }
        }
        else
        {
            if (_actionHotkeys.TryGetValue(_activeActionId.Value, out var seq))
            {
                if (!IsSequencePressed(seq))
                {
                    var releasedId = _activeActionId.Value;
                    _activeActionId = null;
                    HotKeyReleasedDynamic?.Invoke(releasedId);
                }
            }
            else
            {
                _activeActionId = null;
            }
        }
    }

    private bool IsSequencePressed(List<KeyCode> sequence)
    {
        if (sequence.Count == 0) return false;
        return sequence.All(k => _pressedKeys.Contains(k));
    }

    public void Dispose()
    {
        _hook.Dispose();
    }
}
