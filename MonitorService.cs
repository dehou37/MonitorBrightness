
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MonitorBrightness;

public sealed class MonitorDevice
{
    internal IntPtr Handle { get; }
    public string Name { get; }
    public uint Minimum { get; }
    public uint Maximum { get; }

    internal MonitorDevice(
        IntPtr handle, string name, uint minimum, uint maximum)
    {
        Handle = handle;
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
    }

    public int GetBrightness()
    {
        if (!NativeMethods.GetMonitorBrightness(
                Handle, out uint min, out uint current, out uint max))
        {
            throw new InvalidOperationException(
                $"Could not read brightness for {Name}.");
        }

        if (max <= min)
            return 0;

        return (int)Math.Round(
            (current - (double)min) * 100 / (max - min));
    }

    public void SetBrightness(int percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);

        uint target = (uint)Math.Round(
            Minimum + (Maximum - (double)Minimum) * percentage / 100);

        if (!NativeMethods.SetMonitorBrightness(Handle, target))
        {
            throw new InvalidOperationException(
                $"Could not set brightness for {Name}. " +
                "Check that DDC/CI is enabled.");
        }
    }
}

public sealed class MonitorService : IDisposable
{
    private readonly List<MonitorDevice> _monitors = new();
    private bool _disposed;

    public IReadOnlyList<MonitorDevice> Monitors => _monitors;

    public MonitorService()
    {
        NativeMethods.MonitorEnumProc callback =
            (hMonitor, _, _, _) =>
            {
                if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(
                        hMonitor, out uint count) || count == 0)
                    return true;

                var physical = new NativeMethods.PHYSICAL_MONITOR[count];

                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(
                        hMonitor, count, physical))
                    return true;

                foreach (var item in physical)
                {
                    if (NativeMethods.GetMonitorBrightness(
                            item.hPhysicalMonitor,
                            out uint min,
                            out uint current,
                            out uint max)
                        && max > min)
                    {
                        _monitors.Add(new MonitorDevice(
                            item.hPhysicalMonitor,
                            string.IsNullOrWhiteSpace(
                                item.szPhysicalMonitorDescription)
                                ? $"Monitor {_monitors.Count + 1}"
                                : item.szPhysicalMonitorDescription,
                            min, max));
                    }
                    else
                    {
                        // This monitor does not expose usable
                        // hardware brightness controls.
                        NativeMethods.DestroyPhysicalMonitor(
                            item.hPhysicalMonitor);
                    }
                }

                return true;
            };

        try
        {
            if (!NativeMethods.EnumDisplayMonitors(
                    IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            {
                throw new InvalidOperationException(
                    "Windows could not enumerate displays.");
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var monitor in _monitors)
            NativeMethods.DestroyPhysicalMonitor(monitor.Handle);

        _monitors.Clear();
        _disposed = true;
    }
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    internal delegate bool MonitorEnumProc(
        IntPtr hMonitor, IntPtr hdcMonitor,
        IntPtr rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clipRect,
        MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint count,
        [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorBrightness(
        IntPtr monitor, out uint minimum,
        out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorBrightness(
        IntPtr monitor, uint newBrightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitor(
        IntPtr monitor);
}
