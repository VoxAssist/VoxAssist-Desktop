using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class AudioCaptureService
{
    private Process? _recordingProcess;
    private MemoryStream? _audioData;
    public bool IsRecording { get; private set; }

    public event Action<byte[]>? DataAvailable;

    public async Task<List<KeyValuePair<string, string>>> GetAvailableSourcesAsync()
    {
        var sources = new List<KeyValuePair<string, string>> { new("Default", "Default") };
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pactl",
                Arguments = "list sources",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                var blocks = output.Split(new[] { "Source #" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var block in blocks)
                {
                    var lines = block.Split('\n');
                    string name = "";
                    string description = "";
                    
                    foreach(var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Name:")) name = trimmed.Replace("Name: ", "");
                        if (trimmed.StartsWith("Description:")) description = trimmed.Replace("Description: ", "");
                    }

                    if (!string.IsNullOrEmpty(name) && 
                        !name.Contains(".monitor") && 
                        !name.Contains("combined") &&
                        !name.Contains("respeaker_aec"))
                    {
                        sources.Add(new KeyValuePair<string, string>(name, description));
                    }
                }
            }
        }
        catch { }
        return sources;
    }

    public void StartRecording(string source = "Default", int sampleRate = 16000)
    {
        if (IsRecording) return;

        _audioData = new MemoryStream();
        IsRecording = true;

        string deviceArg = source == "Default" ? "" : $"-d {source}";

        _recordingProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "parec",
                Arguments = $"{deviceArg} --rate={sampleRate} --channels=1 --format=s16le --raw",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _recordingProcess.Start();

        Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[4096];
                while (IsRecording && _recordingProcess != null && !_recordingProcess.HasExited)
                {
                    int read = await _recordingProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        var chunk = new byte[read];
                        Array.Copy(buffer, chunk, read);
                        
                        // Fire event for real-time streaming
                        DataAvailable?.Invoke(chunk);

                        lock (_audioData!)
                        {
                            _audioData.Write(buffer, 0, read);
                        }
                    }
                    else if (read == 0) break;
                }
            }
            catch { }
        });
    }

    public byte[] StopRecording()
    {
        if (!IsRecording) return Array.Empty<byte>();
        IsRecording = false;
        
        try
        {
            if (_recordingProcess != null && !_recordingProcess.HasExited)
            {
                _recordingProcess.Kill();
            }
        }
        catch { }

        _recordingProcess?.Dispose();
        _recordingProcess = null;

        if (_audioData == null) return Array.Empty<byte>();

        lock (_audioData)
        {
            var data = _audioData.ToArray();
            _audioData.Dispose();
            _audioData = null;
            return data;
        }
    }
}
