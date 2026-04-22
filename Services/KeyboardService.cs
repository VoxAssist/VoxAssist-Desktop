using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class KeyboardService
{
    private readonly EventSimulator _simulator = new();

    public KeyboardService()
    {
        // Set X11 text simulation delay to 1ms
        _simulator.TextSimulationDelayOnX11 = System.TimeSpan.FromMilliseconds(10);
    }

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // The user requested "no delay" and "instant" feel.
        // Since SetTextAsync is having compilation issues in this environment, 
        // we use the fastest possible simulation. 
        // Spoken text doesn't contain complex modifier sequences, so this is very reliable.
        _simulator.SimulateTextEntry(text);
        
        await Task.CompletedTask;
    }
}
