using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class AecService
{
    private const string SinkName = "respeaker_aec";

    public async Task<bool> EnableAecAsync()
    {
        try
        {
            // 1. Aggressive Cleanup
            await DisableAecAsync();

            // 2. Find sinks
            var sinks = await GetPulseSinksAsync();
            var respeakerSink = sinks.FirstOrDefault(s => s.Contains("SEEED_ReSpeaker"));
            // Try to find a sensible default speaker, or "combined" as fallback
            var defaultSink = sinks.FirstOrDefault(s => s.Contains("Generic_USB_Audio") && s.Contains("Speaker")) 
                             ?? sinks.FirstOrDefault(s => !s.Contains("ReSpeaker") && !s.Contains("monitor"))
                             ?? "combined";

            if (string.IsNullOrEmpty(respeakerSink))
            {
                Console.WriteLine("AEC Error: ReSpeaker sink not found");
                return false;
            }

            // 3. Load module-combine-sink
            // pactl load-module module-combine-sink sink_name=respeaker_aec slaves=<default>,<respeaker>
            await RunPactlAsync($"load-module module-combine-sink sink_name={SinkName} slaves={defaultSink},{respeakerSink}");
            
            // 4. Set as default
            await RunPactlAsync($"set-default-sink {SinkName}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AEC Enable Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsAecLoadedAsync()
    {
        try
        {
            var modules = await RunPactlAsync("list short modules");
            return modules.Contains($"sink_name={SinkName}");
        }
        catch { return false; }
    }

    public async Task DisableAecAsync()
    {
        try
        {
            var modules = await RunPactlAsync("list short modules");
            var lines = modules.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains($"sink_name={SinkName}"))
                {
                    var id = line.Split('\t').FirstOrDefault() ?? line.Split(' ').FirstOrDefault();
                    if (!string.IsNullOrEmpty(id))
                    {
                        await RunPactlAsync($"unload-module {id}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AEC Disable Error: {ex.Message}");
        }
    }

    private async Task<string[]> GetPulseSinksAsync()
    {
        var output = await RunPactlAsync("list short sinks");
        return output.Split('\n')
            .Select(line => line.Split('\t').Skip(1).FirstOrDefault() ?? line.Split(' ').Skip(1).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray()!;
    }

    private async Task<string> RunPactlAsync(string args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pactl",
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) return string.Empty;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
