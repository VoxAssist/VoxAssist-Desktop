using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class SoundService
{
    private readonly string _chirpPath = "/tmp/voxassist_chirp.wav";

    public SoundService()
    {
        PreGenerateChirp();
    }

    private void PreGenerateChirp()
    {
        try
        {
            if (File.Exists(_chirpPath)) File.Delete(_chirpPath);
            
            // Using a more standard ffmpeg command to ensure it creates a valid WAV
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f lavfi -i \"sine=frequency=1000:duration=0.05\" -ar 44100 -ac 1 \"{_chirpPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            process?.WaitForExit();
            
            if (!File.Exists(_chirpPath))
            {
                Console.WriteLine("SoundService: Failed to generate chirp file.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SoundService Error: {ex.Message}");
        }
    }

    public void PlayChirp()
    {
        RunChirp();
    }

    public void PlayDoubleChirp()
    {
        Task.Run(async () =>
        {
            RunChirp();
            await Task.Delay(150);
            RunChirp();
        });
    }

    private void RunChirp()
    {
        if (!File.Exists(_chirpPath)) return;
        
        try
        {
            // paplay is synchronous but we want it non-blocking for the UI
            Task.Run(() => {
                var psi = new ProcessStartInfo
                {
                    FileName = "paplay",
                    Arguments = $"\"{_chirpPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
            });
        }
        catch { }
    }
}
