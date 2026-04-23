using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoxAssist.Desktop.Services;

public class SoundService : IDisposable
{
    private int _chirpSample;
    private int _errorSample;

    public SoundService()
    {
        // Initialize BASS with default device
        if (Bass.Init() || Bass.LastError == Errors.Already)
        {
            // Set a few config options for lower latency
            Bass.Configure(Configuration.PlaybackBufferLength, 100);
            Bass.Configure(Configuration.UpdatePeriod, 10);
            
            // "Warm up" the device by starting/stopping a dummy output
            Bass.Start();
            
            LoadSounds();
        }
    }

    private void LoadSounds()
    {
        try
        {
            _chirpSample = CreateSineSample(1200, 0.01f, 44100); // 30ms Pip at 1200Hz
            _errorSample = CreateSineSample(400, 0.1f, 44100);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SoundService Load Error: {ex.Message}");
        }
    }

    private int CreateSineSample(float frequency, float duration, int sampleRate)
    {
        int channels = 1;
        int bitsPerSample = 16;
        int numSamples = (int)(sampleRate * duration);
        int dataSize = numSamples * channels * (bitsPerSample / 8);

        // Generate Sine Wave Data
        byte[] pcmData = new byte[dataSize];
        for (int i = 0; i < numSamples; i++)
        {
            short value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 32767); // 100% volume
            pcmData[i * 2] = (byte)(value & 0xFF);
            pcmData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }


        // Create a WAV header in memory so BASS can load it as a sample
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // subchunk1size
        writer.Write((short)1); // audioformat (PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8); // byte rate
        writer.Write((short)(channels * bitsPerSample / 8)); // block align
        writer.Write((short)bitsPerSample);
        writer.Write("data".ToCharArray());
        writer.Write(dataSize);
        writer.Write(pcmData);

        byte[] wavBytes = ms.ToArray();
        
        // Load into BASS sample
        GCHandle pinnedArray = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
        try
        {
            return Bass.SampleLoad(pinnedArray.AddrOfPinnedObject(), 0, wavBytes.Length, 3, BassFlags.Default);
        }
        finally
        {
            pinnedArray.Free();
        }
    }

    public void PlayChirp(bool sync = false)
    {
        if (_chirpSample == 0) return;
        
        var channel = Bass.SampleGetChannel(_chirpSample);
        Bass.ChannelPlay(channel);

        if (sync)
        {
            Thread.Sleep(60);
        }
    }

    public void PlayDoubleChirp(bool sync = false)
    {
        if (sync)
        {
            PlayChirp(true);
            Thread.Sleep(50);
            PlayChirp(true);
        }
        else
        {
            Task.Run(async () =>
            {
                PlayChirp(true);
                await Task.Delay(50);
                PlayChirp(true);
            });
        }
    }

    public void PlayError(bool sync = false)
    {
        if (_errorSample == 0) return;

        var channel = Bass.SampleGetChannel(_errorSample);
        Bass.ChannelPlay(channel);

        if (sync)
        {
            Thread.Sleep(210);
        }
    }

    public void StopAll()
    {
        // Stop any active instances of these samples
        if (_chirpSample != 0) Bass.SampleStop(_chirpSample);
        if (_errorSample != 0) Bass.SampleStop(_errorSample);
    }

    public Task PlayAudioAsync(byte[] audioData)
    {
        if (audioData == null || audioData.Length == 0) return Task.CompletedTask;

        var tcs = new TaskCompletionSource<bool>();

        try
        {
            // Stop existing chirps before playing TTS to avoid clashes
            StopAll();
            
            // We MUST pin the data manually and keep it pinned until the stream finishes.
            GCHandle pinned = GCHandle.Alloc(audioData, GCHandleType.Pinned);
            
            // Create a stream from the pinned memory pointer
            int stream = Bass.CreateStream(pinned.AddrOfPinnedObject(), 0, audioData.Length, BassFlags.AutoFree);
            
            if (stream != 0)
            {
                // Explicitly set volume to maximum
                Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, 1.0f);

                // Set a sync to free the GCHandle and complete the task when playback ends
                Bass.ChannelSetSync(stream, SyncFlags.End | SyncFlags.Onetime, 0, (handle, channel, data, user) => {
                    pinned.Free();
                    tcs.TrySetResult(true);
                    Console.WriteLine("SoundService: Playback finished.");
                });

                if (Bass.ChannelPlay(stream))
                {
                    Console.WriteLine($"SoundService: Playing MP3 stream {stream} ({audioData.Length} bytes)");
                }
                else
                {
                    Console.WriteLine($"SoundService: ChannelPlay failed: {Bass.LastError}");
                    pinned.Free();
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Console.WriteLine($"SoundService: CreateStream failed: {Bass.LastError}");
                pinned.Free();
                tcs.TrySetResult(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SoundService: Exception in PlayAudio: {ex.Message}");
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    [Obsolete("Use PlayAudioAsync instead")]
    public void PlayAudio(byte[] audioData) => _ = PlayAudioAsync(audioData);

    public void Dispose()
    {
        if (_chirpSample != 0) Bass.SampleFree(_chirpSample);
        if (_errorSample != 0) Bass.SampleFree(_errorSample);
        Bass.Free();
    }
}
