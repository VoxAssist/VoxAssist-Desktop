using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ManagedBass;
using System.Runtime.InteropServices;

namespace VoxAssist.Desktop.Services;

public class AudioCaptureService : IDisposable
{
    private int _recordHandle;
    private MemoryStream? _audioData;
    private RecordProcedure? _recordProcedure;
    public bool IsRecording { get; private set; }

    public event Action<byte[]>? DataAvailable;

    public AudioCaptureService()
    {
        // Initialize BASS for recording
        // We don't initialize here to allow it to be done on the correct device if needed
    }

    public async Task<List<KeyValuePair<string, string>>> GetAvailableSourcesAsync()
    {
        var sources = new List<KeyValuePair<string, string>> { new("System Default", "Default") };
        
        // BASS device enumeration
        for (int i = 0; Bass.RecordGetDeviceInfo(i, out var info); i++)
        {
            sources.Add(new KeyValuePair<string, string>(i.ToString(), info.Name));
        }

        return sources;
    }

    public void StartRecording(string source = "Default", int sampleRate = 16000)
    {
        if (IsRecording) return;

        try
        {
            _audioData = new MemoryStream();
            
            int deviceIndex = -1; // Default
            if (source != "Default" && int.TryParse(source, out var idx))
            {
                deviceIndex = idx;
            }

            if (!Bass.RecordInit(deviceIndex))
            {
                var error = Bass.LastError;
                if (error != Errors.Already)
                {
                    Console.WriteLine($"BASS RecordInit Error: {error}");
                    return;
                }
            }

            _recordProcedure = new RecordProcedure(MyRecordingProcedure);
            _recordHandle = Bass.RecordStart(sampleRate, 1, BassFlags.Default, _recordProcedure);

            if (_recordHandle == 0)
            {
                Console.WriteLine($"BASS RecordStart Error: {Bass.LastError}");
                return;
            }

            IsRecording = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Capture Start Error: {ex.Message}");
            IsRecording = false;
        }
    }

    private bool MyRecordingProcedure(int handle, IntPtr buffer, int length, IntPtr user)
    {
        if (length > 0 && _audioData != null)
        {
            var data = new byte[length];
            Marshal.Copy(buffer, data, 0, length);
            
            lock (_audioData)
            {
                _audioData.Write(data, 0, length);
            }

            DataAvailable?.Invoke(data);
        }
        return true;
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        if (!IsRecording) return Array.Empty<byte>();
        
        IsRecording = false;
        Bass.ChannelStop(_recordHandle);
        _recordHandle = 0;

        if (_audioData == null) return Array.Empty<byte>();

        lock (_audioData)
        {
            var data = _audioData.ToArray();
            _audioData.Dispose();
            _audioData = null;
            return data;
        }
    }

    public void Dispose()
    {
        if (IsRecording) _ = StopRecordingAsync();
        Bass.RecordFree();
    }
}
