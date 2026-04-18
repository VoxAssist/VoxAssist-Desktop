using System;
using System.Runtime.InteropServices;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.LibUsb;
using System.Threading;
using System.Collections.Generic;

namespace VoxAssist.Desktop.Services;

public class RespeakerService : IDisposable
{
    private UsbDevice? _device;
    private IUsbDevice? _iUsbDevice;
    
    // Vendor IDs
    private const int VID_SEEED_1 = 0x2886; // Original Seeed
    private const int VID_SEEED_2 = 0x28E9; // New Seeed
    private const int VID_XMOS = 0x20B1;    // XMOS Reference

    // Product IDs
    private const int PID_V1 = 0x0007;      // Mic Array v1.0
    private const int PID_V2 = 0x0018;      // Mic Array v2.0
    private const int PID_XVF3800 = 0x0001; // XVF3800
    private const int PID_LITE = 0x0005;    // ReSpeaker Lite
    
    private readonly object _lock = new();

    public bool IsConnected { get; private set; }
    public int MaxBrightness { get; private set; } = 31; // Default to v2 safety

    public RespeakerService()
    {
        TryConnect();
    }

    public bool TryConnect()
    {
        lock (_lock)
        {
            try
            {
                if (_device != null && _device.IsOpen) return true;

                // Define all known ReSpeaker configurations
                var configs = new List<(int vid, int pid, int maxBright)>
                {
                    (VID_SEEED_1, PID_V2, 31),    // v2.0 (The only one with 31 range)
                    (VID_SEEED_1, PID_V1, 255),   // v1.0
                    (VID_SEEED_2, PID_XVF3800, 255),
                    (VID_SEEED_2, PID_LITE, 255),
                    (VID_XMOS, PID_XVF3800, 255),
                    (VID_XMOS, PID_LITE, 255)
                };

                foreach (var config in configs)
                {
                    var finder = new UsbDeviceFinder(config.vid, config.pid);
                    _device = UsbDevice.OpenUsbDevice(finder);
                    
                    if (_device != null)
                    {
                        MaxBrightness = config.maxBright;
                        break;
                    }
                }

                if (_device != null)
                {
                    if (_device is IUsbDevice iUsb)
                    {
                        _iUsbDevice = iUsb;
                        try { iUsb.SetConfiguration(1); } catch { }
                        try { iUsb.ClaimInterface(3); } catch { }
                    }
                    IsConnected = true;
                    return true;
                }
            }
            catch { IsConnected = false; }

            return false;
        }
    }

    private void HandleError()
    {
        IsConnected = false;
        try { _iUsbDevice?.ReleaseInterface(3); } catch { }
        try { _device?.Close(); } catch { }
        _device = null;
        _iUsbDevice = null;
    }

    public void Write(int groupId, int offset, int value)
    {
        if (!IsConnected) return;

        lock (_lock)
        {
            try
            {
                byte[] data = new byte[12];
                BitConverter.TryWriteBytes(data.AsSpan(0), offset);
                BitConverter.TryWriteBytes(data.AsSpan(4), value);
                BitConverter.TryWriteBytes(data.AsSpan(8), 1); // type_id = 1 (int)

                var setup = new UsbSetupPacket
                {
                    RequestType = 0x40, // Vendor, Interface, Out
                    Request = 0,
                    Value = 0,
                    Index = (short)groupId
                };

                int lengthTransferred;
                bool success = _device!.ControlTransfer(ref setup, data, data.Length, out lengthTransferred);
                if (!success) HandleError();
            }
            catch { HandleError(); }
        }
    }

    public int Read(int groupId, int offset)
    {
        if (!IsConnected) return 0;

        lock (_lock)
        {
            try
            {
                int cmd = 0x80 | offset | 0x40; 
                var setup = new UsbSetupPacket
                {
                    RequestType = 0xC0, // Vendor, Interface, In
                    Request = 0,
                    Value = (short)cmd,
                    Index = (short)groupId
                };

                byte[] buffer = new byte[8];
                int lengthTransferred;
                bool success = _device!.ControlTransfer(ref setup, buffer, buffer.Length, out lengthTransferred);

                if (success && lengthTransferred >= 4)
                {
                    return BitConverter.ToInt32(buffer, 0);
                }
                if (!success) HandleError();
            }
            catch { HandleError(); }
            
            return 0;
        }
    }

    public void SetLedMode(int mode, byte[]? data = null)
    {
        if (!IsConnected) return;

        lock (_lock)
        {
            try
            {
                var setup = new UsbSetupPacket
                {
                    RequestType = 0x40,
                    Request = 0,
                    Value = (short)mode,
                    Index = 0x1C // LED Control Group
                };

                int lengthTransferred;
                bool success = _device!.ControlTransfer(ref setup, data ?? new byte[] { 0 }, (data?.Length ?? 1), out lengthTransferred);
                if (!success) HandleError();
            }
            catch { HandleError(); }
        }
    }

    public void SetLedMono(byte r, byte g, byte b)
    {
        SetLedMode(1, new byte[] { r, g, b, 0 });
    }

    public void SetLedBrightness(byte brightness)
    {
        // Verified from Parakeet/Python project: 
        // Command 0x20, Range 0x00 - 0x1F (0-31) or 0-255 depending on model.
        // Many XMOS firmwares expect 4-byte alignment for parameters.
        byte clamped = (byte)Math.Min((int)brightness, MaxBrightness);

        lock (_lock)
        {
            try
            {
                var setup = new UsbSetupPacket
                {
                    RequestType = 0x40, // Vendor, Interface, Out
                    Request = 0,
                    Value = 0x20,       // Brightness Command
                    Index = 0x1C        // LED Control Group
                };

                // Use a 4-byte buffer as many firmwares require 4-byte alignment/size
                byte[] data = new byte[] { clamped, 0, 0, 0 };
                int lengthTransferred;
                bool success = _device!.ControlTransfer(ref setup, data, data.Length, out lengthTransferred);

                // Fallback: If 0x20 (direct command) doesn't work, try register write (offset 1)
                // which was known to work but might switch modes.
                if (!success)
                {
                    Write(0x1C, 1, clamped);
                }
            }
            catch { HandleError(); }
        }
    }

    public int GetDoaAngle() => Read(21, 0);
    public int GetFreezeState() => Read(19, 6);
    public int GetAgcState() => Read(19, 0);
    public int GetNsState() => Read(19, 8);

    public void Dispose()
    {
        lock (_lock)
        {
            try { _iUsbDevice?.ReleaseInterface(3); } catch { }
            try { _device?.Close(); } catch { }
            UsbDevice.Exit();
        }
    }
}
