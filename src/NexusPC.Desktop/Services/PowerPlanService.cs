using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NexusPC.Desktop.Services;

public sealed record PowerPlan(string Name, Guid Id, bool IsActive);

public sealed class PowerPlanService
{
    private static readonly Dictionary<Guid, string> KnownPlans = new()
    {
        [Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e")] = "Dengeli",
        [Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a")] = "Güç tasarrufu",
        [Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")] = "Yüksek performans",
        [Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61")] = "Nihai performans"
    };
    private static string PowerCfg => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

    public async Task<IReadOnlyList<PowerPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunAsync("/list", cancellationToken);
        var active = await RunAsync("/getactivescheme", cancellationToken);
        var activeId = Regex.Matches(active, "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
            .Select(m => Guid.Parse(m.Value)).FirstOrDefault();
        var present = Regex.Matches(output, "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
            .Select(m => Guid.Parse(m.Value)).Distinct().Where(KnownPlans.ContainsKey)
            .Select(id => new PowerPlan(KnownPlans[id], id, id == activeId)).ToList();
        return present;
    }

    public async Task SetPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!KnownPlans.ContainsKey(id)) throw new InvalidOperationException("Güç planı izin listesinde bulunmuyor.");
        await RunAsync($"/setactive {id:D}", cancellationToken);
    }

    private static async Task<string> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(PowerCfg)) throw new FileNotFoundException("Windows güç yönetim aracı bulunamadı.");
        using var process = new Process { StartInfo = new ProcessStartInfo(PowerCfg, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException((await stderr).Trim());
        return output;
    }
}
