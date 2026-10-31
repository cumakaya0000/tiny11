using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace NexusPC.Desktop.Services;

public sealed record OllamaModel(string Name, long Size, string ModifiedAt)
{
    public string SizeText => $"{Size / 1024d / 1024 / 1024:N2} GB · Yerel model";
}
public sealed record LocalChatTurn(string Role, string Content, DateTimeOffset CreatedAt);

public sealed class LocalChatHistoryStore
{
    private static string ChatDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUSPC", "Chats");

    public async Task<List<LocalChatTurn>> LoadAsync(string model, CancellationToken cancellationToken = default)
    {
        var path = GetPath(model);
        if (!File.Exists(path)) return new List<LocalChatTurn>();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<LocalChatTurn>>(stream, cancellationToken: cancellationToken) ?? new();
        }
        catch (JsonException) { return new List<LocalChatTurn>(); }
    }

    public async Task SaveAsync(string model, IReadOnlyList<LocalChatTurn> turns, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ChatDirectory);
        var path = GetPath(model);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, turns.TakeLast(500).ToList(), cancellationToken: cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task ClearAsync(string model, CancellationToken cancellationToken = default)
    {
        var path = GetPath(model);
        if (File.Exists(path)) await Task.Run(() => File.Delete(path), cancellationToken);
    }

    private static string GetPath(string model)
    {
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(model))).ToLowerInvariant();
        return Path.Combine(ChatDirectory, $"{digest}.json");
    }
}

public sealed class OllamaClient
{
    private static readonly HttpClient Client = new() { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = Timeout.InfiniteTimeSpan };

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync("/api/version", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("version").GetString() ?? "Bilinmiyor";
    }

    public async Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("models").EnumerateArray().Select(x => new OllamaModel(
            x.GetProperty("name").GetString() ?? "?", x.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            x.TryGetProperty("modified_at", out var modified) ? modified.GetString() ?? "" : "")).ToList();
    }

    public async Task PullAsync(string model, IProgress<string> progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        { Content = new StringContent(JsonSerializer.Serialize(new { name = model, stream = true }), Encoding.UTF8, "application/json") };
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0) continue;
            using var item = JsonDocument.Parse(line);
            var root = item.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : "İşleniyor";
            if (root.TryGetProperty("total", out var total) && root.TryGetProperty("completed", out var completed) && total.GetInt64() > 0)
                status += $" — {completed.GetInt64() / 1024d / 1024 / 1024:N2} / {total.GetInt64() / 1024d / 1024 / 1024:N2} GB";
            progress.Report(status ?? "İşleniyor");
            if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        }
    }

    public async Task DeleteModelAsync(string model, CancellationToken cancellationToken = default)
    {
        using var response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
        { Content = new StringContent(JsonSerializer.Serialize(new { name = model }), Encoding.UTF8, "application/json") }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> ChatAsync(string model, IReadOnlyList<LocalChatTurn> turns, string keepAlive = "5m", CancellationToken cancellationToken = default)
    {
        using var response = await Client.PostAsync("/api/chat", new StringContent(JsonSerializer.Serialize(new
        { model, stream = false, messages = turns.Select(t => new { role = t.Role, content = t.Content }), keep_alive = keepAlive }), Encoding.UTF8, "application/json"), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "Yanıt boş.";
    }
}
