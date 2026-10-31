using System.Diagnostics;
using System.IO;

namespace NexusPC.Desktop.Services;

public sealed record CatalogApp(string Name, string Publisher, string PackageId, string Category, string Description);

public sealed class WingetCatalogService
{
    // Fixed package IDs prevent user input from becoming a command or arbitrary package identifier.
    public static IReadOnlyList<CatalogApp> Apps { get; } =
    [
        new("7-Zip", "Igor Pavlov", "7zip.7zip", "Araçlar", "Dosya arşivleme ve sıkıştırma."),
        new("Google Chrome", "Google", "Google.Chrome", "Tarayıcılar", "Web tarayıcısı."),
        new("Mozilla Firefox", "Mozilla", "Mozilla.Firefox", "Tarayıcılar", "Açık kaynaklı web tarayıcısı."),
        new("VLC media player", "VideoLAN", "VideoLAN.VLC", "Medya", "Yerel medya oynatıcı."),
        new("Visual Studio Code", "Microsoft", "Microsoft.VisualStudioCode", "Geliştirme", "Kod düzenleyici."),
        new("Steam", "Valve", "Valve.Steam", "Oyun", "Oyun mağazası ve başlatıcısı."),
        new("Discord", "Discord Inc.", "Discord.Discord", "İletişim", "Sesli ve yazılı iletişim uygulaması."),
        new("Spotify", "Spotify AB", "Spotify.Spotify", "Medya", "Müzik uygulaması.")
    ];

    private static string? FindWinget() =>
        Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } local
            ? Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe")
            : null;

    public bool IsAvailable => FindWinget() is { } path && File.Exists(path);

    public async Task<int> InstallAsync(CatalogApp app, CancellationToken cancellationToken = default)
    {
        if (!Apps.Any(x => x.PackageId == app.PackageId))
            throw new InvalidOperationException("Paket katalog izin listesinde bulunmuyor.");
        var winget = FindWinget();
        if (winget is null || !File.Exists(winget)) throw new FileNotFoundException("WinGet bulunamadı.");

        var start = new ProcessStartInfo(winget)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        start.ArgumentList.Add("install");
        start.ArgumentList.Add("--id");
        start.ArgumentList.Add(app.PackageId);
        start.ArgumentList.Add("--exact");
        start.ArgumentList.Add("--source");
        start.ArgumentList.Add("winget");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet başlatılamadı.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
