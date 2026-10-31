using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace NexusPC.Desktop.Services;

public sealed record OptimizationOption(string Id, string Title, string Description, IReadOnlyList<string> AffectedSettings)
{
    public string AffectedSettingsSummary => string.Join(", ", AffectedSettings);
}
public sealed record OptimizationBackup(string ActionId, string Title, DateTimeOffset CreatedAt, List<RegistryValueSnapshot> Values);
public sealed record RegistryValueSnapshot(string KeyPath, string ValueName, string? OriginalValue, int OriginalKind, bool Existed, string AppliedValue);
public sealed record BackupListItem(string FilePath, OptimizationBackup Backup)
{
    public string DisplayText => $"{Backup.Title} · {Backup.CreatedAt.LocalDateTime:g}";
}

public sealed class OptimizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static IReadOnlyList<OptimizationOption> Catalog { get; } = new[]
    {
        new OptimizationOption("privacy.suggestions", "Başlat menüsü önerilerini azalt", "Windows'un önerilen uygulama ve tanıtım içeriklerini bu kullanıcı hesabı için kapatır. Kişisel dosyalara veya diğer hesaplara dokunmaz.", new[] { "Windows içerik önerileri: 3 kullanıcı değeri" }),
        new OptimizationOption("ui.widgets", "Görev çubuğundaki Widget düğmesini gizle", "Widget düğmesini yalnızca bu kullanıcı hesabının görev çubuğunda gizler. Widget bileşenini kaldırmaz.", new[] { "Görev çubuğu Widget görünürlüğü" }),
        new OptimizationOption("ui.darkMode", "Windows koyu görünümünü aç", "Windows uygulama ve sistem arayüzünün koyu görünüm tercihini bu kullanıcı hesabında açar.", new[] { "Windows uygulama teması", "Windows sistem teması" }),
        new OptimizationOption("ui.fileExtensions", "Dosya uzantılarını göster", "Dosya Gezgini'nde bilinen dosya türlerinin uzantılarını bu kullanıcı hesabı için görünür yapar.", new[] { "Dosya Gezgini uzantı görünürlüğü" }),
        new OptimizationOption("gaming.gameMode", "Windows Oyun Modu'nu aç", "Windows Oyun Modu tercihini bu kullanıcı hesabı için açar; diğer oyun veya güvenlik ayarlarını değiştirmez.", new[] { "Windows Oyun Modu kullanıcı tercihleri" })
    };

    private static string BackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUSPC", "Backups");

    public async Task<IReadOnlyList<BackupListItem>> GetBackupsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(BackupDirectory)) return Array.Empty<BackupListItem>();
        var result = new List<BackupListItem>();
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(file).Length > 1024 * 1024) continue;
                await using var stream = File.OpenRead(file);
                var data = await JsonSerializer.DeserializeAsync<OptimizationBackup>(stream, JsonOptions, cancellationToken);
                if (data is not null && IsValidBackup(data)) result.Add(new BackupListItem(file, data));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return result;
    }

    public async Task<string> ApplyAsync(string actionId, CancellationToken cancellationToken = default)
    {
        var option = Catalog.SingleOrDefault(x => x.Id == actionId) ?? throw new InvalidOperationException("Bu optimizasyon eylemi izin listesinde bulunmuyor.");
        var targets = GetTargets(actionId);
        var snapshotValues = targets.Select(t => Capture(t.Path, t.Name, t.Applied)).ToList();
        var backup = new OptimizationBackup(actionId, option.Title, DateTimeOffset.Now, snapshotValues);
        Directory.CreateDirectory(BackupDirectory);
        var file = Path.Combine(BackupDirectory, $"{actionId.Replace('.', '-')}_{DateTimeOffset.Now:yyyyMMdd_HHmmss_fff}.json");
        await using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, cancellationToken);
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = Registry.CurrentUser.CreateSubKey(target.Path, writable: true) ?? throw new IOException("Kullanıcı ayarı açılamadı.");
                key.SetValue(target.Name, target.Applied, RegistryValueKind.DWord);
            }
        }
        catch
        {
            await RestoreSnapshotAsync(backup, CancellationToken.None);
            throw;
        }
        return $"'{option.Title}' uygulandı. Önceki değerlerin yedeği oluşturuldu.";
    }

    public async Task<string> RestoreAsync(BackupListItem item, CancellationToken cancellationToken = default)
    {
        if (!Path.GetFullPath(item.FilePath).StartsWith(Path.GetFullPath(BackupDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Yedek dosyası NEXUS PC yedek klasörünün dışında.");
        if (!IsValidBackup(item.Backup)) throw new InvalidOperationException("Yedek dosyasındaki işlem veya kayıt değerleri izin listesiyle eşleşmiyor.");
        await RestoreSnapshotAsync(item.Backup, cancellationToken);
        return $"'{item.Backup.Title}' için geri alma tamamlandı. Sonradan değiştirilmiş değerler korunmuştur.";
    }

    private static Task RestoreSnapshotAsync(OptimizationBackup backup, CancellationToken cancellationToken)
    {
        foreach (var item in backup.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var key = Registry.CurrentUser.OpenSubKey(item.KeyPath, writable: true);
            var current = key?.GetValue(item.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (!Equals(Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture), item.AppliedValue)) continue;
            if (!item.Existed) key?.DeleteValue(item.ValueName, throwOnMissingValue: false);
            else
            {
                using var restoreKey = Registry.CurrentUser.CreateSubKey(item.KeyPath, writable: true);
                restoreKey?.SetValue(item.ValueName, RestoreValue(item.OriginalValue, (RegistryValueKind)item.OriginalKind), (RegistryValueKind)item.OriginalKind);
            }
        }
        return Task.CompletedTask;
    }

    private static object RestoreValue(string? value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => int.Parse(value!, System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => long.Parse(value!, System.Globalization.CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => value ?? "",
        _ => throw new InvalidOperationException("Yedekte bu kayıt türünü geri yükleyemiyorum.")
    };

    private static RegistryValueSnapshot Capture(string path, string name, int applied)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        var existed = key?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        var kind = existed ? key!.GetValueKind(name) : RegistryValueKind.DWord;
        var raw = existed ? key!.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null;
        var original = existed ? Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) : null;
        if (existed && kind is not (RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new InvalidOperationException($"'{name}' kayıt türü bu sürümde güvenli yedeklemeyi desteklemiyor.");
        return new RegistryValueSnapshot(path, name, original, (int)kind, existed, applied.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static bool IsValidBackup(OptimizationBackup backup)
    {
        if (backup.Values is null || !Catalog.Any(x => x.Id == backup.ActionId)) return false;
        var targets = GetTargets(backup.ActionId);
        if (backup.Values.Count != targets.Count) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in backup.Values)
        {
            if (!targets.Any(t => t.Path == value.KeyPath && t.Name == value.ValueName && t.Applied.ToString(System.Globalization.CultureInfo.InvariantCulture) == value.AppliedValue)) return false;
            if (!seen.Add(value.KeyPath + "\n" + value.ValueName)) return false;
            if ((RegistryValueKind)value.OriginalKind is not (RegistryValueKind.DWord or RegistryValueKind.QWord or RegistryValueKind.String or RegistryValueKind.ExpandString)) return false;
            if (!value.Existed && value.OriginalValue is not null) return false;
        }
        return true;
    }

    private static IReadOnlyList<(string Path, string Name, int Applied)> GetTargets(string id) => id switch
    {
        "privacy.suggestions" => new[]
        {
            (@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
            (@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
            (@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0)
        },
        "ui.widgets" => new[] { (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0) },
        "ui.darkMode" => new[]
        {
            ( @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0),
            ( @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0)
        },
        "ui.fileExtensions" => new[] { ( @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0) },
        "gaming.gameMode" => new[]
        {
            (@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
            (@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1)
        },
        _ => throw new InvalidOperationException("Bu optimizasyon eylemi izin listesinde bulunmuyor.")
    };
}
