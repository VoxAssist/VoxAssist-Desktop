using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class PermissionService
{
    public static bool IsLinuxAndMissingCapabilities()
    {
        if (!OperatingSystem.IsLinux()) return false;
        
        // If we can already open uinput, we don't need to do anything.
        if (UInputDevice.HasPermissions()) return false;

        return true;
    }

    public static async Task<bool> ElevateAndRestartAsync()
    {
        if (!OperatingSystem.IsLinux()) return false;

        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentPath)) return false;

        // 1. Get paths
        var installDir = Path.GetDirectoryName(currentPath);
        var nativeDir = Path.Combine(installDir!, "Native");
        var launcherSrc = Path.Combine(nativeDir, "vox-launch.c");
        var setupScript = Path.Combine(nativeDir, "setup-launcher.sh");

        if (!File.Exists(launcherSrc) || !File.Exists(setupScript)) {
            // Fallback to development paths
            launcherSrc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Native", "vox-launch.c");
            setupScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Native", "setup-launcher.sh");
        }

        if (!File.Exists(setupScript)) {
            Console.WriteLine($"Elevation Error: Could not find setup-launcher.sh at {setupScript}");
            return false;
        }

        string voxassistDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "voxassist");
        try
        {
            if (!Directory.Exists(voxassistDir))
            {
                Directory.CreateDirectory(voxassistDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not create voxassist directory: {ex.Message}");
        }

        var launcherBin = Path.Combine(voxassistDir, "vox-launch");
        string appPath = Environment.GetEnvironmentVariable("APPIMAGE") ?? currentPath;

        // Check for gcc before starting
        try {
            var checkGcc = new ProcessStartInfo {
                FileName = "which",
                Arguments = "gcc",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var checkProcess = Process.Start(checkGcc);
            if (checkProcess != null) {
                await checkProcess.WaitForExitAsync();
                if (checkProcess.ExitCode != 0) {
                    Console.WriteLine("Elevation Error: gcc is not installed. Please install 'build-essential'.");
                    return false;
                }
            }
        } catch { }

        // 2. The command: run the setup script with pkexec
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                Arguments = $"/bin/bash \"{setupScript}\" \"{launcherSrc}\" \"{launcherBin}\" \"{appPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var process = Process.Start(psi);
            if (process == null) return false;

            string stdOut = await process.StandardOutput.ReadToEndAsync();
            string stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Elevation Failed (Exit {process.ExitCode})");
                if (!string.IsNullOrEmpty(stdOut)) Console.WriteLine($"Stdout: {stdOut}");
                if (!string.IsNullOrEmpty(stdErr)) Console.WriteLine($"Stderr: {stdErr}");
                return false;
            }

            // Success... restart via launcher
            Process.Start(new ProcessStartInfo { FileName = launcherBin, Arguments = $"\"{appPath}\"", UseShellExecute = true });
            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Elevation failed: {ex.Message}");
        }

        return false;
    }
}
