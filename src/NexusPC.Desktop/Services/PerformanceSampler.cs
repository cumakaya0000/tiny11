using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace NexusPC.Desktop.Services;

public sealed record PerformanceReading(double CpuPercent, double RamUsedGb, double RamTotalGb, double NetworkDownMbps, double NetworkUpMbps);

public sealed class PerformanceSampler
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeFileTime { public uint Low; public uint High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    private ulong _lastIdle, _lastTotal;
    private long _lastReceived, _lastSent;
    private DateTime _lastSample = DateTime.UtcNow;

    public PerformanceReading Sample()
    {
        var cpu = 0d;
        if (OperatingSystem.IsWindows() && GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value;
            if (_lastTotal > 0 && total > _lastTotal)
                cpu = Math.Clamp(100d * (total - _lastTotal - (idle.Value - _lastIdle)) / (total - _lastTotal), 0, 100);
            _lastTotal = total; _lastIdle = idle.Value;
        }
        var (totalRamGb, availableRamGb) = SystemSnapshotReader.ReadMemory();
        var received = 0L; var sent = 0L;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try { var stats = adapter.GetIPv4Statistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
            catch (NetworkInformationException) { }
        }
        var elapsed = Math.Max((DateTime.UtcNow - _lastSample).TotalSeconds, 0.1);
        var down = _lastReceived == 0 ? 0 : Math.Max(0, (received - _lastReceived) * 8d / elapsed / 1_000_000);
        var up = _lastSent == 0 ? 0 : Math.Max(0, (sent - _lastSent) * 8d / elapsed / 1_000_000);
        _lastReceived = received; _lastSent = sent; _lastSample = DateTime.UtcNow;
        return new PerformanceReading(cpu, Math.Max(0, totalRamGb - availableRamGb), totalRamGb, down, up);
    }
}
