using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Text;
using NexusPC.Desktop.Services;

namespace NexusPC.Desktop;

public partial class LocalAiWindow : Window
{
    private readonly OllamaClient _ollama = new();
    private readonly LocalChatHistoryStore _history = new();
    private CancellationTokenSource? _pullCancellation;
    private CancellationTokenSource? _chatCancellation;
    private List<LocalChatTurn> _turns = new();
    private string? _historyModel;
    private bool _loadingHistory;

    public LocalAiWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshModelsAsync();
    }

    private async Task RefreshModelsAsync()
    {
        try
        {
            var version = await _ollama.GetVersionAsync();
            var models = await _ollama.GetModelsAsync();
            ConnectionText.Text = $"● Ollama {version} bağlı · {models.Count} model yüklü";
            ModelsList.ItemsSource = models;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ConnectionText.Text = "Ollama API kullanılamıyor. Ollama'yı başlatıp yeniden dene.";
            ModelsList.ItemsSource = Array.Empty<OllamaModel>();
        }
        catch (Exception ex) { ConnectionText.Text = $"Ollama yanıtı okunamadı: {ex.Message}"; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshModelsAsync();

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        var model = ModelNameBox.Text.Trim();
        if (model.Length is < 2 or > 120 || model.Any(char.IsWhiteSpace))
        { PullStatus.Text = "Geçerli bir Ollama model kimliği gir."; return; }
        if (MessageBox.Show($"'{model}' kimliği Ollama'nın model kataloğuna gönderilecek ve indirilecek. Boyut, kullanım koşulları ve lisans modele göre değişir; indirmeden önce model yayıncısının lisansını ve indirme boyutunu kontrol et. İşlem internet ve disk alanı kullanır. Başlatılsın mı?", "Model indirme", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        _pullCancellation = new CancellationTokenSource();
        CancelButton.Visibility = Visibility.Visible;
        PullStatus.Text = "Ollama'dan indirme akışı bekleniyor…";
        var progress = new Progress<string>(status => PullStatus.Text = status);
        try
        {
            await _ollama.PullAsync(model, progress, _pullCancellation.Token);
            PullStatus.Text = "Model indirme tamamlandı.";
            await RefreshModelsAsync();
        }
        catch (OperationCanceledException) { PullStatus.Text = "İndirme iptal edildi. Ollama, indirilen katmanları sonraki denemede kullanabilir."; }
        catch (Exception ex) { PullStatus.Text = $"İndirme başarısız: {ex.Message}"; }
        finally { _pullCancellation?.Dispose(); _pullCancellation = null; CancelButton.Visibility = Visibility.Collapsed; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _pullCancellation?.Cancel();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsList.SelectedItem is not OllamaModel model) { PullStatus.Text = "Önce kaldırılacak modeli seç."; return; }
        if (MessageBox.Show($"'{model.Name}' modeli Ollama'dan silinecek. Bu işlem model dosyalarını kaldırır. Emin misin?", "Modeli kaldır", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _ollama.DeleteModelAsync(model.Name); await RefreshModelsAsync(); PullStatus.Text = "Model kaldırıldı."; }
        catch (Exception ex) { PullStatus.Text = $"Model kaldırılamadı: {ex.Message}"; }
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsList.SelectedItem is not OllamaModel model) { AnswerBox.Text = "Sohbet için önce bir model seç."; return; }
        if (string.IsNullOrWhiteSpace(PromptBox.Text)) { AnswerBox.Text = "Bir mesaj yaz."; return; }
        if (_historyModel != model.Name) await LoadHistoryAsync(model.Name);
        var prompt = PromptBox.Text.Trim();
        _turns.Add(new LocalChatTurn("user", prompt, DateTimeOffset.Now));
        PromptBox.Clear();
        AnswerBox.Text = FormatConversation() + "\n\nYerel model yanıtlıyor…";
        _chatCancellation = new CancellationTokenSource();
        ChatButton.IsEnabled = false;
        CancelChatButton.Visibility = Visibility.Visible;
        try
        {
            await _history.SaveAsync(model.Name, _turns);
            var keepAlive = KeepAliveButton.Tag?.ToString() ?? "5m";
            var answer = await _ollama.ChatAsync(model.Name, _turns, keepAlive, _chatCancellation.Token);
            _turns.Add(new LocalChatTurn("assistant", answer, DateTimeOffset.Now));
            await _history.SaveAsync(model.Name, _turns);
            AnswerBox.Text = FormatConversation();
        }
        catch (OperationCanceledException) { AnswerBox.Text = $"Yanıt iptal edildi.\n\n{FormatConversation()}"; }
        catch (Exception ex) { AnswerBox.Text = $"Sohbet hatası: {ex.Message}\n\n{FormatConversation()}"; }
        finally
        {
            _chatCancellation?.Dispose();
            _chatCancellation = null;
            ChatButton.IsEnabled = true;
            CancelChatButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelChat_Click(object sender, RoutedEventArgs e) => _chatCancellation?.Cancel();

    private void KeepAlive_Click(object sender, RoutedEventArgs e)
    {
        var choices = new[] { (Value: "0", Label: "Hemen boşalt"), (Value: "5m", Label: "5 dakika"), (Value: "30m", Label: "30 dakika"), (Value: "-1", Label: "Süresiz") };
        var current = Array.FindIndex(choices, x => x.Value == KeepAliveButton.Tag?.ToString());
        var next = choices[(current + 1 + choices.Length) % choices.Length];
        KeepAliveButton.Tag = next.Value;
        KeepAliveButton.Content = $"{next.Label} ▾";
    }

    private async void ModelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedModelText is not null && ModelsList.SelectedItem is OllamaModel model)
        {
            SelectedModelText.Text = $"Seçili: {model.Name} · Bellekte 5 dakika tutulur.";
            await LoadHistoryAsync(model.Name);
        }
    }

    private async Task LoadHistoryAsync(string model)
    {
        if (_loadingHistory || _historyModel == model) return;
        _loadingHistory = true;
        try
        {
            _turns = await _history.LoadAsync(model);
            _historyModel = model;
            AnswerBox.Text = FormatConversation();
            SelectedModelText.Text = $"Seçili: {model} · Yerel geçmiş: {_turns.Count / 2} tur.";
        }
        catch (Exception ex) { AnswerBox.Text = $"Sohbet geçmişi okunamadı: {ex.Message}"; }
        finally { _loadingHistory = false; }
    }

    private string FormatConversation()
    {
        var output = new StringBuilder();
        foreach (var turn in _turns)
            output.Append(turn.Role == "user" ? "Sen: " : "NEXUS AI: ").AppendLine(turn.Content).AppendLine();
        return output.ToString().TrimEnd();
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsList.SelectedItem is not OllamaModel model) { AnswerBox.Text = "Önce bir model seç."; return; }
        if (MessageBox.Show($"'{model.Name}' modelinin bu bilgisayarda saklanan sohbet geçmişi silinecek. Bu işlem geri alınamaz. Devam edilsin mi?", "Sohbet geçmişini sil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _history.ClearAsync(model.Name);
            _turns.Clear();
            _historyModel = model.Name;
            AnswerBox.Clear();
            SelectedModelText.Text = $"Seçili: {model.Name} · Yerel geçmiş temizlendi.";
        }
        catch (Exception ex) { AnswerBox.Text = $"Geçmiş silinemedi: {ex.Message}"; }
    }
}
