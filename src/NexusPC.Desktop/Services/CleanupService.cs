using System.IO;

namespace NexusPC.Desktop.Services;

public sealed record CleanupCandidate(string FullPath, long SizeBytes, DateTime LastWriteTime)
{
    public string Name => Path.GetFileName(FullPath);
    public string SizeText => $"{SizeBytes / 1024d / 1024:N1} MB";
}

public sealed record CleanupReport(IReadOnlyList<CleanupCandidate> Files, long TotalBytes, int SkippedCount)
{
    public string TotalText => $"{TotalBytes / 1024d / 1024:N1} MB";
}

public sealed class CleanupService
{
    private const int MinimumAgeDays = 14;

    public Task<CleanupReport> ScanAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var roots = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") }
            .Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var files = new List<CleanupCandidate>();
        var skipped = 0;
        var cutoff = DateTime.Now.AddDays(-MinimumAgeDays);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> paths;
            try { paths = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly).ToArray(); }
            catch (IOException) { skipped++; continue; }
            catch (UnauthorizedAccessException) { skipped++; continue; }
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTime > cutoff || info.Length > 2L * 1024 * 1024 * 1024) { skipped++; continue; }
                    files.Add(new CleanupCandidate(info.FullName, info.Length, info.LastWriteTime));
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
        }
        var ordered = files.OrderByDescending(x => x.SizeBytes).ToList();
        return new CleanupReport(ordered, ordered.Sum(x => x.SizeBytes), skipped);
    }, cancellationToken);

    public Task<(int Deleted, long Freed, int Failed)> DeleteSelectedAsync(IEnumerable<CleanupCandidate> selected, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var roots = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") }
            .Where(Directory.Exists).Select(x => Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        var cutoff = DateTime.Now.AddDays(-MinimumAgeDays);
        var deleted = 0; long freed = 0; var failed = 0;
        foreach (var candidate in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = Path.GetFullPath(candidate.FullPath);
                if (!roots.Any(root => path.StartsWith(root, StringComparison.OrdinalIgnoreCase))) { failed++; continue; }
                var info = new FileInfo(path);
                if (!info.Exists || info.DirectoryName is null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTime > cutoff || info.Length > 2L * 1024 * 1024 * 1024) { failed++; continue; }
                var size = info.Length;
                info.Delete();
                deleted++; freed += size;
            }
            catch (IOException) { failed++; }
            catch (UnauthorizedAccessException) { failed++; }
        }
        return (deleted, freed, failed);
    }, cancellationToken);
}
