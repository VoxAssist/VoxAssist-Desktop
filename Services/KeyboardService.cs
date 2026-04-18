using SharpHook;
using SharpHook.Native;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class KeyboardService
{
    private readonly EventSimulator _simulator = new();

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        // Brief delay like in Python version
        await Task.Delay(100);
        
        // SharpHook.Simulator.SimulateTextEntry is very convenient
        _simulator.SimulateTextEntry(text);
    }
}
