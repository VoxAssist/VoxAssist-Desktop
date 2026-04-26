using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace VoxAssist.Desktop.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
}

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private const string RepoOwner = "VoxAssist";
    private const string RepoName = "VoxAssist-Desktop";

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VoxAssist", "1.0"));
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var version = tag.TrimStart('v');

            if (IsNewer(version, currentVersion))
            {
                var assetName = GetPlatformAssetName();
                var assets = root.GetProperty("assets").EnumerateArray();
                var asset = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == assetName);

                if (asset.ValueKind != JsonValueKind.Undefined)
                {
                    return new UpdateInfo
                    {
                        Version = version,
                        ReleaseNotes = root.GetProperty("body").GetString() ?? "",
                        DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "",
                        FileName = assetName
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
        }

        return null;
    }

    private bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
        {
            return vLatest > vCurrent;
        }
        return false;
    }

    private string GetPlatformAssetName()
    {
        if (OperatingSystem.IsWindows()) return "VoxAssist-Windows.exe";
        if (OperatingSystem.IsLinux()) return "VoxAssist-Linux";
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 
                ? "VoxAssist-macOS-AppleSilicon" 
                : "VoxAssist-macOS-Intel";
        }
        return "";
    }

    public async Task ApplyUpdateAsync(UpdateInfo info, Action<double> progressCallback)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentPath)) throw new Exception("Could not determine current process path.");

        var tempPath = currentPath + ".new";
        
        // 1. Download
        using (var response = await _httpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[8192];
                var totalRead = 0L;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;
                    if (totalBytes != -1) progressCallback((double)totalRead / totalBytes);
                }
            }
        }

        // 2. Prepare Swap
        if (OperatingSystem.IsWindows())
        {
            var batchFile = Path.Combine(Path.GetTempPath(), "voxassist_update.bat");
            var script = $@"
@echo off
timeout /t 1 /nobreak > nul
del ""{currentPath}""
move /y ""{tempPath}"" ""{currentPath}""
start """" ""{currentPath}""
del ""%~f0""
";
            File.WriteAllText(batchFile, script);
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{batchFile}\"", CreateNoWindow = true, UseShellExecute = false });
        }
        else
        {
            var shellFile = Path.Combine(Path.GetTempPath(), "voxassist_update.sh");
            var script = $@"
#!/bin/bash
sleep 1
mv ""{tempPath}"" ""{currentPath}""
chmod +x ""{currentPath}""
""{currentPath}"" &
rm ""$0""
";
            File.WriteAllText(shellFile, script);
            // Ensure the update script is executable
            Process.Start("chmod", $"+x \"{shellFile}\"").WaitForExit();
            Process.Start("/bin/bash", $"\"{shellFile}\"");
        }

        // 3. Exit and let the script take over
        Environment.Exit(0);
    }
}
