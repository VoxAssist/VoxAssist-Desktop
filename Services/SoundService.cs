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
    private int _tickSample;
    private int _keepAliveSample;
    private int _keepAliveChannel;
    private Timer? _keepAliveTimer;

    public SoundService()
    {
        // Initialize BASS with default device
        if (Bass.Init() || Bass.LastError == Errors.Already)
        {
            // Set a few config options for lower latency
            Bass.Configure(Configuration.PlaybackBufferLength, 50);
            Bass.Configure(Configuration.UpdatePeriod, 5);
            
            // "Warm up" the device by starting/stopping a dummy output
            Bass.Start();
            
            LoadSounds();
            StartKeepAlive();
        }
    }

    /// <summary>
    /// PipeWire/Pulse suspend idle or digitally-silent streams. A looping ultrasonic
    /// tone at tiny amplitude keeps the output node alive without being audible.
    /// Volume 0 is not enough — that is still digital silence and gets corked.
    /// </summary>
    private void StartKeepAlive()
    {
        try
        {
            _keepAliveSample = CreateSineSample(17000f, 0.25f, 44100, amplitude: 0.004f);
            if (_keepAliveSample == 0) return;

            EnsureKeepAlivePlaying(forceRestart: true);

            _keepAliveTimer = new Timer(_ =>
            {
                try { EnsureKeepAlivePlaying(forceRestart: false); }
                catch { /* ignore */ }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SoundService keep-alive failed: {ex.Message}");
        }
    }

    private void EnsureKeepAlivePlaying(bool forceRestart)
    {
        if (_keepAliveSample == 0) return;

        if (_keepAliveChannel == 0)
        {
            _keepAliveChannel = Bass.SampleGetChannel(_keepAliveSample);
            if (_keepAliveChannel == 0) return;
            Bass.ChannelFlags(_keepAliveChannel, BassFlags.Loop, BassFlags.Loop);
        }

        if (forceRestart || Bass.ChannelIsActive(_keepAliveChannel) != PlaybackState.Playing)
        {
            Bass.Start();
            Bass.ChannelPlay(_keepAliveChannel, true);
        }
    }

    private void LoadSounds()
    {
        try
        {
            // Long enough to survive a PipeWire graph hiccup when capture starts.
            _chirpSample = CreateSineSample(1200, 0.05f, 44100, envelopeMs: 4f);
            _errorSample = CreateSineSample(400, 0.1f, 44100, envelopeMs: 4f);
            _tickSample = CreateSineSample(800, 0.008f, 44100, envelopeMs: 2f);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SoundService Load Error: {ex.Message}");
        }
    }

    private int CreateSineSample(float frequency, float duration, int sampleRate, float amplitude = 1f, float envelopeMs = 0f)
    {
        int channels = 1;
        int bitsPerSample = 16;
        int numSamples = Math.Max(1, (int)(sampleRate * duration));
        int dataSize = numSamples * channels * (bitsPerSample / 8);
        int envSamples = envelopeMs > 0 ? Math.Max(1, (int)(sampleRate * (envelopeMs / 1000f))) : 0;

        byte[] pcmData = new byte[dataSize];
        for (int i = 0; i < numSamples; i++)
        {
            float env = 1f;
            if (envSamples > 0)
            {
                if (i < envSamples) env = i / (float)envSamples;
                else if (i > numSamples - envSamples) env = (numSamples - i) / (float)envSamples;
            }

            short value = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 32767 * amplitude * env);
            pcmData[i * 2] = (byte)(value & 0xFF);
            pcmData[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write((short)bitsPerSample);
        writer.Write("data".ToCharArray());
        writer.Write(dataSize);
        writer.Write(pcmData);

        byte[] wavBytes = ms.ToArray();

        GCHandle pinnedArray = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
        try
        {
            return Bass.SampleLoad(pinnedArray.AddrOfPinnedObject(), 0, wavBytes.Length, 8, BassFlags.SampleOverrideLowestVolume);
        }
        finally
        {
            pinnedArray.Free();
        }
    }

    public void PlayChirp(bool sync = false)
    {
        if (_chirpSample == 0) return;

        Bass.Start();
        EnsureKeepAlivePlaying(forceRestart: false);

        var channel = Bass.SampleGetChannel(_chirpSample);
        if (channel == 0)
        {
            Bass.SampleStop(_chirpSample);
            channel = Bass.SampleGetChannel(_chirpSample);
            if (channel == 0)
            {
                Console.WriteLine($"PlayChirp: SampleGetChannel failed: {Bass.LastError}");
                return;
            }
        }

        Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, 1f);
        if (!Bass.ChannelPlay(channel, true))
            Console.WriteLine($"PlayChirp: ChannelPlay failed: {Bass.LastError}");

        if (sync)
            Thread.Sleep(70);
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

    public void PlayTick()
    {
        if (_tickSample == 0) return;
        var channel = Bass.SampleGetChannel(_tickSample);
        Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, 0.3f); // Tick should be subtle
        Bass.ChannelPlay(channel);
    }

    public void StopAll()
    {
        // Stop any active instances of these samples
        if (_chirpSample != 0) Bass.SampleStop(_chirpSample);
        if (_errorSample != 0) Bass.SampleStop(_errorSample);
        if (_tickSample != 0) Bass.SampleStop(_tickSample);
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
        try { _keepAliveTimer?.Dispose(); } catch { /* ignore */ }
        if (_keepAliveChannel != 0)
        {
            try { Bass.ChannelStop(_keepAliveChannel); } catch { /* ignore */ }
            _keepAliveChannel = 0;
        }
        if (_keepAliveSample != 0) Bass.SampleFree(_keepAliveSample);
        if (_chirpSample != 0) Bass.SampleFree(_chirpSample);
        if (_errorSample != 0) Bass.SampleFree(_errorSample);
        if (_tickSample != 0) Bass.SampleFree(_tickSample);
        Bass.Free();
    }
}
