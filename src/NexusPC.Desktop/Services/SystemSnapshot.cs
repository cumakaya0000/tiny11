using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NexusPC.Desktop.Services;

public sealed record SystemSnapshot(string OperatingSystem, string Processor, string Architecture, double TotalRamGb, double AvailableRamGb, IReadOnlyList<string> GraphicsAdapters, IReadOnlyList<DiskSnapshot> Disks);
public sealed record DiskSnapshot(string Name, long FreeBytes, long TotalBytes)
{
    public string FreeText => $"{FreeBytes / 1024d / 1024 / 1024:N1} GB boş / {TotalBytes / 1024d / 1024 / 1024:N1} GB";
}

public static class SystemSnapshotReader
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

    public static SystemSnapshot Read()
    {
        string cpu = "Bilinmiyor";
        var osName = Environment.OSVersion.VersionString.Replace("Microsoft ", "");
        var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = key?.GetValue("ProcessorNameString") as string ?? cpu;
            using var version = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = version?.GetValue("ProductName") as string;
            var displayVersion = version?.GetValue("DisplayVersion") as string;
            var build = version?.GetValue("CurrentBuildNumber") as string;
            var updateBuild = version?.GetValue("UBR");
            if (!string.IsNullOrWhiteSpace(product))
                osName = $"{product}{(string.IsNullOrWhiteSpace(displayVersion) ? "" : $" {displayVersion}")}{(string.IsNullOrWhiteSpace(build) ? "" : $" (build {build}{(updateBuild is null ? "" : $".{updateBuild}")})")}";
        }
        var (totalGb, availableGb) = ReadMemory();
        var gpuNames = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            using var graphics = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (graphics is not null)
                foreach (var adapter in graphics.GetSubKeyNames())
                    using (var device = graphics.OpenSubKey($@"{adapter}\0000"))
                        if (device?.GetValue("DriverDesc") is string name && !gpuNames.Contains(name)) gpuNames.Add(name);
        }
        var disks = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => new DiskSnapshot($"{d.Name} ({d.VolumeLabel})".TrimEnd(' ', '(', ')'), d.AvailableFreeSpace, d.TotalSize)).ToList();
        return new SystemSnapshot(osName, cpu, arch, totalGb, availableGb, gpuNames, disks);
    }

    public static (double TotalGb, double AvailableGb) ReadMemory()
    {
        var memory = new MemoryStatus();
        return OperatingSystem.IsWindows() && GlobalMemoryStatusEx(memory)
            ? (memory.TotalPhysical / 1024d / 1024 / 1024, memory.AvailablePhysical / 1024d / 1024 / 1024)
            : (0, 0);
    }
}
