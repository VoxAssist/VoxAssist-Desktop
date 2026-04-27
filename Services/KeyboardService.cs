using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace VoxAssist.Desktop.Services;

public class KeyboardService : IDisposable
{
    private readonly EventSimulator? _simulator;
    private readonly UInputDevice? _uinput;

    public KeyboardService()
    {
        if (OperatingSystem.IsLinux())
        {
            if (UInputDevice.HasPermissions())
            {
                try
                {
                    _uinput = new UInputDevice();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"KeyboardService: Failed to initialize uinput: {ex.Message}");
                    _simulator = new EventSimulator();
                    _simulator.TextSimulationDelayOnX11 = TimeSpan.FromMilliseconds(40);
                }
            }
            else
            {
                _simulator = new EventSimulator();
                _simulator.TextSimulationDelayOnX11 = TimeSpan.FromMilliseconds(40);
            }
        }
        else
        {
            _simulator = new EventSimulator();
            _simulator.TextSimulationDelayOnX11 = TimeSpan.FromMilliseconds(40);
        }
    }

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_uinput != null)
        {
            await _uinput.TypeTextAsync(text);
        }
        else if (_simulator != null)
        {
            _simulator.SimulateTextEntry(text);
            await Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        _uinput?.Dispose();
    }
}

internal class UInputDevice : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct input_event
    {
        public nint tv_sec;
        public nint tv_usec;
        public ushort type;
        public ushort code;
        public int value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct uinput_setup
    {
        public ushort id_bustype;
        public ushort id_vendor;
        public ushort id_product;
        public ushort id_version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string name;
        public uint ff_effects_max;
    }

    private const int O_WRONLY = 0x0001;
    private const int O_NONBLOCK = 0x0800;

    private const uint UI_SET_EVBIT = 0x40045564;
    private const uint UI_SET_KEYBIT = 0x40045565;
    private const uint UI_DEV_SETUP = 0x405c5503;
    private const uint UI_DEV_CREATE = 0x5501;
    private const uint UI_DEV_DESTROY = 0x5502;

    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort SYN_REPORT = 0x00;

    private const ushort KEY_LEFTSHIFT = 42;

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, int arg);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, ref uinput_setup arg);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, ref input_event buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private int _fd = -1;

    public static bool HasPermissions()
    {
        // Check if we were passed an inherited file descriptor
        string? fdEnv = Environment.GetEnvironmentVariable("VOXASSIST_UINPUT_FD");
        if (!string.IsNullOrEmpty(fdEnv)) return true;

        try
        {
            int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
            if (fd >= 0)
            {
                close(fd);
                return true;
            }
            int error = Marshal.GetLastPInvokeError();
            Console.WriteLine($"UInputDevice: Open failed with errno {error}");
            return false;
        }
        catch (Exception ex)
        { 
            Console.WriteLine($"UInputDevice: Permission check exception: {ex.Message}");
            return false; 
        }
    }

    public UInputDevice()
    {
        string? fdEnv = Environment.GetEnvironmentVariable("VOXASSIST_UINPUT_FD");
        if (!string.IsNullOrEmpty(fdEnv) && int.TryParse(fdEnv, out int inheritedFd))
        {
            _fd = inheritedFd;
            Console.WriteLine($"UInputDevice: Using inherited file descriptor {_fd}");
            return;
        }

        _fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (_fd < 0) throw new Exception("Could not open /dev/uinput");

        if (ioctl(_fd, UI_SET_EVBIT, EV_KEY) < 0) throw new Exception("UI_SET_EVBIT failed");
        
        // Register common keys
        for (ushort i = 1; i < 120; i++)
        {
            ioctl(_fd, UI_SET_KEYBIT, i);
        }

        var setup = new uinput_setup
        {
            name = "VoxAssist Virtual Keyboard",
            id_bustype = 0x03, // BUS_USB
            id_vendor = 0x1234,
            id_product = 0x5678,
            id_version = 1
        };

        if (ioctl(_fd, UI_DEV_SETUP, ref setup) < 0) throw new Exception("UI_DEV_SETUP failed");
        if (ioctl(_fd, UI_DEV_CREATE, 0) < 0) throw new Exception("UI_DEV_CREATE failed");
    }

    public async Task TypeTextAsync(string text)
    {
        foreach (char c in text)
        {
            if (_keyMap.TryGetValue(c, out var info))
            {
                if (info.shift) SendKey(KEY_LEFTSHIFT, true);
                SendKey(info.code, true);
                SendKey(info.code, false);
                if (info.shift) SendKey(KEY_LEFTSHIFT, false);
                
                // Minimal delay
                await Task.Delay(1);
            }
        }
    }

    private void SendKey(ushort code, bool pressed)
    {
        var ev = new input_event
        {
            type = EV_KEY,
            code = code,
            value = pressed ? 1 : 0
        };
        if (write(_fd, ref ev, (nuint)Marshal.SizeOf(ev)) < 0)
        {
            Console.WriteLine($"UInputDevice: write(EV_KEY) failed, errno {Marshal.GetLastPInvokeError()}");
        }

        var syn = new input_event
        {
            type = EV_SYN,
            code = SYN_REPORT,
            value = 0
        };
        if (write(_fd, ref syn, (nuint)Marshal.SizeOf(syn)) < 0)
        {
            Console.WriteLine($"UInputDevice: write(EV_SYN) failed, errno {Marshal.GetLastPInvokeError()}");
        }
    }

    public void Dispose()
    {
        if (_fd >= 0)
        {
            ioctl(_fd, UI_DEV_DESTROY, 0);
            close(_fd);
            _fd = -1;
        }
    }

    private static readonly Dictionary<char, (ushort code, bool shift)> _keyMap = new()
    {
        { 'a', (30, false) }, { 'b', (48, false) }, { 'c', (46, false) }, { 'd', (32, false) },
        { 'e', (18, false) }, { 'f', (33, false) }, { 'g', (34, false) }, { 'h', (35, false) },
        { 'i', (23, false) }, { 'j', (36, false) }, { 'k', (37, false) }, { 'l', (38, false) },
        { 'm', (50, false) }, { 'n', (49, false) }, { 'o', (24, false) }, { 'p', (25, false) },
        { 'q', (16, false) }, { 'r', (19, false) }, { 's', (31, false) }, { 't', (20, false) },
        { 'u', (22, false) }, { 'v', (47, false) }, { 'w', (17, false) }, { 'x', (45, false) },
        { 'y', (21, false) }, { 'z', (44, false) },
        { 'A', (30, true) }, { 'B', (48, true) }, { 'C', (46, true) }, { 'D', (32, true) },
        { 'E', (18, true) }, { 'F', (33, true) }, { 'G', (34, true) }, { 'H', (35, true) },
        { 'I', (23, true) }, { 'J', (36, true) }, { 'K', (37, true) }, { 'L', (38, true) },
        { 'M', (50, true) }, { 'N', (49, true) }, { 'O', (24, true) }, { 'P', (25, true) },
        { 'Q', (16, true) }, { 'R', (19, true) }, { 'S', (31, true) }, { 'T', (20, true) },
        { 'U', (22, true) }, { 'V', (47, true) }, { 'W', (17, true) }, { 'X', (45, true) },
        { 'Y', (21, true) }, { 'Z', (44, true) },
        { '1', (2, false) }, { '2', (3, false) }, { '3', (4, false) }, { '4', (5, false) },
        { '5', (6, false) }, { '6', (7, false) }, { '7', (8, false) }, { '8', (9, false) },
        { '9', (10, false) }, { '0', (11, false) },
        { '!', (2, true) }, { '@', (3, true) }, { '#', (4, true) }, { '$', (5, true) },
        { '%', (6, true) }, { '^', (7, true) }, { '&', (8, true) }, { '*', (9, true) },
        { '(', (10, true) }, { ')', (11, true) },
        { ' ', (57, false) }, { '.', (52, false) }, { ',', (51, false) }, { '?', (53, true) },
        { '\n', (28, false) }, { '\r', (28, false) }, { '\t', (15, false) },
        { '-', (12, false) }, { '_', (12, true) }, { '=', (13, false) }, { '+', (13, true) },
        { '[', (26, false) }, { '{', (26, true) }, { ']', (27, false) }, { '}', (27, true) },
        { '\\', (43, false) }, { '|', (43, true) }, { ';', (39, false) }, { ':', (39, true) },
        { '\'', (40, false) }, { '"', (40, true) }, { '/', (53, false) }, { '<', (51, true) },
        { '>', (52, true) }, { '`', (41, false) }, { '~', (41, true) }
    };
}
