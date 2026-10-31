using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NexusPC.Desktop.Services;

namespace NexusPC.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient LocalApi = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly PerformanceSampler _sampler = new();
    private readonly OptimizationService _optimizations = new();
    private readonly CleanupService _cleanup = new();
    private readonly PowerPlanService _power = new();
    private readonly WingetCatalogService _catalog = new();
    private readonly Dictionary<string, TextBlock> _liveValues = new();
    private string _currentPage = "home";
    private PerformanceReading _reading = new(0, 0, 0, 0, 0);
    private IReadOnlyList<CleanupCandidate> _cleanupCandidates = Array.Empty<CleanupCandidate>();
    private ListBox? _cleanupList;
    private TextBlock? _cleanupStatus;
    private Button? _cleanupDeleteButton;
    private StackPanel? _appsList;
    private TextBlock? _appsStatus;
    private string _appsQuery = "";

    public MainWindow()
    {
        InitializeComponent();
        _liveTimer.Tick += (_, _) => UpdateLiveMetrics();
        _liveTimer.Start();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckOllamaAsync();
        await NavigateAsync("home");
    }

    private async Task CheckOllamaAsync()
    {
        try
        {
            using var response = await LocalApi.GetAsync("http://127.0.0.1:11434/api/version");
            AiStatus.Text = response.IsSuccessStatusCode ? "● Ollama bağlı" : "Ollama yanıt vermiyor";
            AiStatus.Foreground = response.IsSuccessStatusCode ? Brushes.LightGreen : Brushes.Orange;
        }
        catch (HttpRequestException) { AiStatus.Text = "Ollama bulunamadı"; AiStatus.Foreground = Brushes.Orange; }
        catch (TaskCanceledException) { AiStatus.Text = "Ollama zaman aşımına uğradı"; AiStatus.Foreground = Brushes.Orange; }
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) await NavigateAsync(page);
    }

    private async Task NavigateAsync(string page)
    {
        _currentPage = page;
        foreach (var button in new[] { HomeNav, OptimizeNav, TweaksNav, GamingNav, PowerNav, CleanupNav, AppsNav, HistoryNav, AnalysisNav, UpdatesNav, SettingsNav })
        {
            var selected = Equals(button.Tag, page);
            button.Background = selected ? new SolidColorBrush(Color.FromRgb(53, 22, 34)) : new SolidColorBrush(Color.FromRgb(28, 23, 28));
            button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(124, 41, 68)) : new SolidColorBrush(Color.FromRgb(48, 39, 46));
        }
        _liveValues.Clear(); _cleanupList = null; _cleanupStatus = null; _cleanupDeleteButton = null;
        switch (page)
        {
            case "home": SetHeader("NEXUS PC / KONTROL PANELİ", "Bilgisayarın. Kontrol sende.", "Canlı sistem göstergeleri ve hızlı erişim."); PageHost.Content = Scroll(BuildHomePage()); break;
            case "optimize": SetHeader("01 / OPTİMİZASYON", "PC'n için bir puan.", "Önerileri incele, istediğini tek tek uygula."); PageHost.Content = Scroll(BuildOptimizePage()); break;
            case "tweaks": SetHeader("02 / İNCE AYARLAR", "Windows'u kendine göre ayarla.", "Her değişiklik açıkça listelenir ve geri alınabilir."); PageHost.Content = Scroll(BuildTweaksPage()); break;
            case "gaming": SetHeader("03 / OYUN", "Canlı performans takibi.", "CPU, bellek ve ağ kullanımını izle; oyun modunu yönet."); PageHost.Content = Scroll(BuildGamingPage()); break;
            case "power": SetHeader("04 / GÜÇ", "Güç planını seç.", "Plan değişmeden önce mevcut seçim gösterilir."); PageHost.Content = Scroll(BuildPowerPage()); await LoadPowerPlansAsync(); break;
            case "cleanup": SetHeader("05 / TEMİZLİK", "Geçici dosyaları gözden geçir.", "14 günden eski dosyalar listelenir; seçim yapmadan silinmez."); PageHost.Content = Scroll(BuildCleanupPage()); break;
            case "apps": SetHeader("06 / UYGULAMALAR", "Uygulama kataloğu.", "Sabit paket kataloğunda ara; kurulumdan önce seçimini onayla."); PageHost.Content = Scroll(BuildAppsPage()); RefreshAppsList(); break;
            case "history": SetHeader("NEXUS PC / GERİ ALMA", "Yedeklerin ve değişiklik geçmişin.", "Ayar yedekleri bu Windows kullanıcı hesabında tutulur."); PageHost.Content = await BuildHistoryPageAsync(); break;
            case "analysis": SetHeader("NEXUS PC / DONANIM", "Sistem analizi.", "Bilgiler bilgisayardan okunur; ayarlarda değişiklik yapılmaz."); PageHost.Content = BuildAnalysisPage(); break;
            case "updates": SetHeader("NEXUS PC / GÜNCELLEME", "Sürüm bilgileri.", "Güvenli imzalı güncelleme kanalı henüz yapılandırılmadı."); PageHost.Content = Scroll(BuildUpdatesPage()); break;
            case "settings": SetHeader("NEXUS PC / AYARLAR", "Gizlilik ve uygulama tercihleri.", "Yerel veriler ve bağlantı bilgileri."); PageHost.Content = Scroll(BuildSettingsPage()); break;
            default: await NavigateAsync("home"); break;
        }
        RefreshButton.Visibility = page is "home" or "gaming" or "analysis" or "power" ? Visibility.Visible : Visibility.Collapsed;
        if (page is "home" or "gaming") UpdateLiveMetrics();
    }

    private void SetHeader(string kicker, string title, string description)
    { PageKicker.Text = kicker; PageTitle.Text = title; PageDescription.Text = description; }

    private ScrollViewer Scroll(Panel child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    private StackPanel NewStack() => new() { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 6, 16) };

    private Border Card(string title, string detail, string? key = null, string? value = null)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(162, 155, 164)) });
        if (value is not null)
        {
            var val = new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 3), TextWrapping = TextWrapping.Wrap };
            if (key is not null) _liveValues[key] = val;
            content.Children.Add(val);
        }
        content.Children.Add(new TextBlock { Text = detail, Foreground = new SolidColorBrush(Color.FromRgb(129, 121, 132)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        return new Border { Background = new SolidColorBrush(Color.FromRgb(21, 18, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13), Padding = new Thickness(17), Child = content, Margin = new Thickness(0, 0, 10, 10) };
    }

    private TextBlock SectionLabel(string text) => new() { Text = text.ToUpperInvariant(), Foreground = new SolidColorBrush(Color.FromRgb(233, 91, 132)), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(2, 14, 0, 10) };

    private TextBlock Body(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(162, 155, 164)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private Button ActionButton(string text, RoutedEventHandler handler)
    { var button = new Button { Content = text, Margin = new Thickness(0, 8, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Center }; button.Click += handler; return button; }

    private StackPanel BuildHomePage()
    {
        var stack = NewStack();
        var summary = SystemSnapshotReader.Read();
        var hero = new Border { Background = new SolidColorBrush(Color.FromRgb(23, 20, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(51, 37, 44)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(23), Margin = new Thickness(0, 0, 0, 18) };
        hero.Child = new StackPanel { Children = { new TextBlock { Text = "NEXUS PC / CANLI DURUM", Foreground = new SolidColorBrush(Color.FromRgb(233, 91, 132)), FontSize = 10, FontWeight = FontWeights.Bold }, new TextBlock { Text = summary.OperatingSystem, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap }, Body($"{summary.Processor} · {summary.Architecture}") } };
        stack.Children.Add(hero);
        var cards = new UniformGrid { Columns = 4 };
        cards.Children.Add(Card("CPU", "Anlık işlemci yükü", "cpu", "Ölçülüyor"));
        cards.Children.Add(Card("RAM", "Kullanılan / toplam fiziksel bellek", "ram", "Ölçülüyor"));
        cards.Children.Add(Card("AĞ", "İndirme / yükleme hızı", "network", "Ölçülüyor"));
        var diskText = summary.Disks.FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", StringComparison.OrdinalIgnoreCase))?.FreeText ?? "Bilgi alınamadı";
        cards.Children.Add(Card("SİSTEM SÜRÜCÜSÜ", "Boş / toplam alan", null, diskText));
        stack.Children.Add(cards);
        stack.Children.Add(SectionLabel("Hızlı erişim"));
        var quick = new UniformGrid { Columns = 2 };
        quick.Children.Add(QuickCard("Optimizasyon", "Bellek, disk, ağ, başlangıç ve arka plan durumuna bak.", "optimize"));
        quick.Children.Add(QuickCard("Akıllı temizlik", "Geçici dosyaları tara, seçtiklerini gözden geçir.", "cleanup"));
        quick.Children.Add(QuickCard("İnce ayarlar", "İzin listesindeki geri alınabilir ayarlar.", "tweaks"));
        quick.Children.Add(QuickCard("Oyun performansı", "Canlı sistem kullanımını ve Oyun Modu tercihlerini gör.", "gaming"));
        stack.Children.Add(quick);
        return stack;
    }

    private Border QuickCard(string title, string description, string page)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold }); panel.Children.Add(Body(description));
        var button = ActionButton("Aç  →", async (_, _) => await NavigateAsync(page)); panel.Children.Add(button);
        return new Border { Background = new SolidColorBrush(Color.FromRgb(21, 18, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = panel, Margin = new Thickness(0, 0, 10, 10) };
    }

    private void UpdateLiveMetrics()
    {
        if (_currentPage is not ("home" or "gaming")) return;
        try { _reading = _sampler.Sample(); }
        catch { return; }
        if (_liveValues.TryGetValue("cpu", out var cpu)) cpu.Text = $"{_reading.CpuPercent:N0}%";
        if (_liveValues.TryGetValue("ram", out var ram)) ram.Text = $"{_reading.RamUsedGb:N1} / {_reading.RamTotalGb:N1} GB";
        if (_liveValues.TryGetValue("network", out var network)) network.Text = $"↓ {_reading.NetworkDownMbps:N2} / ↑ {_reading.NetworkUpMbps:N2} Mbps";
    }

    private StackPanel BuildOptimizePage()
    {
        var stack = NewStack();
        var snapshot = SystemSnapshotReader.Read();
        var systemDrive = snapshot.Disks.FirstOrDefault(d => d.Name.StartsWith(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", StringComparison.OrdinalIgnoreCase));
        var score = 100;
        if (snapshot.TotalRamGb > 0 && snapshot.AvailableRamGb / snapshot.TotalRamGb < .12) score -= 20;
        if (systemDrive is not null && systemDrive.FreeBytes / (double)Math.Max(systemDrive.TotalBytes, 1) < .08) score -= 25;
        var scoreCard = Card("NEXUS DURUM PUANI", "Basit bellek ve depolama göstergelerine dayalı yerel özet; benchmark veya garanti değildir.", null, $"{score} / 100");
        scoreCard.Margin = new Thickness(0, 0, 0, 12); stack.Children.Add(scoreCard);
        var categories = new UniformGrid { Columns = 3 };
        categories.Children.Add(Card("Performans", $"CPU {_reading.CpuPercent:N0}% kullanım", null, "Canlı"));
        categories.Children.Add(Card("Bellek", $"{snapshot.AvailableRamGb:N1} GB kullanılabilir", null, "Sistem ölçümü"));
        categories.Children.Add(Card("Depolama", systemDrive?.FreeText ?? "Disk bilgisi alınamadı", null, "Sistem sürücüsü"));
        categories.Children.Add(Card("Ağ", $"İndirme {_reading.NetworkDownMbps:N2} · Yükleme {_reading.NetworkUpMbps:N2} Mbps", null, "Canlı"));
        categories.Children.Add(Card("Başlangıç", "Uygulama başlangıçları henüz ölçülmüyor.", null, "Bilgi yok"));
        categories.Children.Add(Card("Arka plan", "Arka plan süreç sınıflandırması henüz etkin değil.", null, "Bilgi yok"));
        stack.Children.Add(categories);
        stack.Children.Add(SectionLabel("Geri alınabilir öneriler"));
        foreach (var option in OptimizationService.Catalog) stack.Children.Add(BuildOptimizationRow(option));
        stack.Children.Add(Body("Bu sürümde Windows ayarlarını değiştiren her işlem yalnızca tek tek onayla çalışır. Otomatik toplu değişiklik yapılmaz."));
        return stack;
    }

    private Border BuildOptimizationRow(OptimizationOption option)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = option.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Body(option.Description));
        var button = ActionButton("Önizle ve uygula", async (_, _) => await ApplyOptimizationAsync(option));
        panel.Children.Add(button);
        return new Border { Background = new SolidColorBrush(Color.FromRgb(21, 18, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(17), Child = panel, Margin = new Thickness(0, 0, 0, 10) };
    }

    private async Task ApplyOptimizationAsync(OptimizationOption option)
    {
        var answer = MessageBox.Show($"'{option.Title}' uygulanacak.\n\nEtkilenecek ayarlar:\n• {option.AffectedSettingsSummary}\n\nMevcut değerler önce NEXUSPC\\Backups klasörüne kaydedilecek. Devam edilsin mi?", "Değişikliği önizle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try { MessageBox.Show(await _optimizations.ApplyAsync(option.Id), "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show($"İşlem uygulanamadı: {ex.Message}", "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private StackPanel BuildTweaksPage()
    {
        var stack = NewStack();
        stack.Children.Add(Body("Katalogdaki her anahtar açıklanır. Yedekler bu Windows hesabına kaydedilir ve geri alma sırasında daha sonra değiştirilmiş değerler korunur."));
        stack.Children.Add(SectionLabel("Gizlilik · Arayüz · Sistem · Oyun"));
        foreach (var option in OptimizationService.Catalog) stack.Children.Add(BuildOptimizationRow(option));
        stack.Children.Add(Body("Daha geniş ince ayar kataloğu henüz eklenmedi. Güvenlik, Windows Update, UAC veya Defender ayarlarını kapatan anahtarlar varsayılan olarak sunulmaz."));
        return stack;
    }

    private StackPanel BuildGamingPage()
    {
        var stack = NewStack();
        stack.Children.Add(Body("Bu ekran gerçek zamanlı CPU, RAM ve ağ kullanımını gösterir. GPU telemetrisi ve oyun süreci algılama bu sürümde mevcut değil."));
        var metrics = new UniformGrid { Columns = 3 };
        metrics.Children.Add(Card("CPU", "Anlık işlemci kullanımı", "cpu", "Ölçülüyor"));
        metrics.Children.Add(Card("BELLEK", "Kullanılan / toplam", "ram", "Ölçülüyor"));
        metrics.Children.Add(Card("AĞ", "İndirme / yükleme", "network", "Ölçülüyor"));
        stack.Children.Add(metrics);
        stack.Children.Add(SectionLabel("Oyun modu"));
        stack.Children.Add(BuildOptimizationRow(OptimizationService.Catalog.Single(x => x.Id == "gaming.gameMode")));
        var powerButton = ActionButton("Güç planlarını aç", async (_, _) => await NavigateAsync("power"));
        stack.Children.Add(powerButton);
        return stack;
    }

    private StackPanel BuildPowerPage()
    {
        var stack = NewStack();
        stack.Children.Add(Body("Windows'ta bulunan hazır planları listeler. Geçişten önce onay istenir; mevcut plan ayrıca bu sayfada gösterilir."));
        stack.Children.Add(SectionLabel("Kullanılabilir planlar"));
        stack.Children.Add(new TextBlock { Name = "PowerPlanLoading", Text = "Planlar okunuyor…", Foreground = new SolidColorBrush(Color.FromRgb(162, 155, 164)), Margin = new Thickness(0, 6, 0, 12) });
        return stack;
    }

    private async Task LoadPowerPlansAsync()
    {
        IReadOnlyList<PowerPlan> plans;
        try { plans = await _power.GetPlansAsync(); }
        catch (Exception ex)
        {
            if (PageHost.Content is ScrollViewer { Content: StackPanel errorStack })
            {
                var loadingText = errorStack.Children.OfType<TextBlock>().FirstOrDefault(x => x.Name == "PowerPlanLoading");
                if (loadingText is not null) loadingText.Text = $"Güç planları okunamadı: {ex.Message}";
            }
            return;
        }
        if (PageHost.Content is not ScrollViewer { Content: StackPanel stack }) return;
        var loading = stack.Children.OfType<TextBlock>().FirstOrDefault(x => x.Name == "PowerPlanLoading");
        if (loading is null) return;
        var index = stack.Children.IndexOf(loading); stack.Children.Remove(loading);
        if (plans.Count == 0) { stack.Children.Insert(index, Body("Windows güç planları okunamadı veya yerleşik plan bulunamadı.")); return; }
        var rows = new UniformGrid { Columns = 2 };
        foreach (var plan in plans)
        {
            var row = Card(plan.Name, plan.IsActive ? "Şu anda etkin" : "Hazır plan", null, plan.IsActive ? "ETKİN" : "Kullanılabilir");
            if (!plan.IsActive)
            {
                if (row.Child is StackPanel p) p.Children.Add(ActionButton("Bu planı seç", async (_, _) => await SetPowerPlanAsync(plan)));
            }
            rows.Children.Add(row);
        }
        stack.Children.Insert(index, rows);
    }

    private async Task SetPowerPlanAsync(PowerPlan plan)
    {
        if (MessageBox.Show($"Windows güç planı '{plan.Name}' olarak değiştirilecek. Devam edilsin mi?", "Güç planını değiştir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await _power.SetPlanAsync(plan.Id); await NavigateAsync("power"); }
        catch (Exception ex) { MessageBox.Show($"Güç planı değiştirilemedi: {ex.Message}", "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private StackPanel BuildCleanupPage()
    {
        var stack = NewStack();
        stack.Children.Add(Body("Yalnızca geçici klasörlerin en üst düzeyindeki, 14 günden eski dosyalar taranır. Klasörler, kısayollar, yeniden yönlendirilmiş dosyalar ve son 14 gündeki dosyalar atlanır."));
        stack.Children.Add(ActionButton("Geçici dosyaları tara", CleanupScan_Click));
        _cleanupStatus = new TextBlock { Text = "Henüz tarama yapılmadı.", Foreground = new SolidColorBrush(Color.FromRgb(162, 155, 164)), Margin = new Thickness(0, 12, 0, 8), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(_cleanupStatus);
        _cleanupList = new ListBox { MinHeight = 180, MaxHeight = 430, Background = new SolidColorBrush(Color.FromRgb(17, 16, 20)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)) };
        stack.Children.Add(_cleanupList);
        _cleanupDeleteButton = ActionButton("Seçili dosyaları sil…", CleanupDelete_Click); _cleanupDeleteButton.IsEnabled = false;
        stack.Children.Add(_cleanupDeleteButton);
        return stack;
    }

    private async void CleanupScan_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupStatus is null || _cleanupList is null) return;
        _cleanupStatus.Text = "Geçici klasörler taranıyor…"; _cleanupDeleteButton!.IsEnabled = false; _cleanupList.Items.Clear();
        try
        {
            var report = await _cleanup.ScanAsync(); _cleanupCandidates = report.Files;
            foreach (var file in report.Files)
            {
                var check = new CheckBox { Tag = file, IsChecked = false, Margin = new Thickness(5, 4, 5, 4) };
                check.Content = $"{file.Name}   ·   {file.SizeText}   ·   {file.LastWriteTime:g}";
                check.Checked += (_, _) => UpdateCleanupSelection(); check.Unchecked += (_, _) => UpdateCleanupSelection();
                _cleanupList.Items.Add(check);
            }
            _cleanupStatus.Text = $"{report.Files.Count} dosya bulundu · {report.TotalText} olası alan · {report.SkippedCount} dosya güvenlik/erişim nedeniyle atlandı. Hiçbiri seçili değil.";
        }
        catch (Exception ex) { _cleanupStatus.Text = $"Tarama başarısız: {ex.Message}"; }
    }

    private void UpdateCleanupSelection()
    {
        if (_cleanupList is null || _cleanupStatus is null || _cleanupDeleteButton is null) return;
        var selected = _cleanupList.Items.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (CleanupCandidate)x.Tag).ToArray();
        _cleanupDeleteButton.IsEnabled = selected.Length > 0;
        _cleanupStatus.Text = $"{_cleanupCandidates.Count} aday · seçili {selected.Length} dosya, {selected.Sum(x => x.SizeBytes) / 1024d / 1024:N1} MB";
    }

    private async void CleanupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupList is null) return;
        var selected = _cleanupList.Items.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (CleanupCandidate)x.Tag).ToArray();
        if (selected.Length == 0) return;
        var total = selected.Sum(x => x.SizeBytes) / 1024d / 1024;
        if (MessageBox.Show($"{selected.Length} seçili geçici dosya silinecek (yaklaşık {total:N1} MB). Yalnızca listelenen dosyalar etkilenecek; klasörler silinmeyecek. Devam edilsin mi?", "Temizliği onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var result = await _cleanup.DeleteSelectedAsync(selected);
            _cleanupStatus!.Text = $"Silinen: {result.Deleted} · boşalan: {result.Freed / 1024d / 1024:N1} MB · atlanan/hatalı: {result.Failed}";
            await CleanupScanAgainAsync();
        }
        catch (Exception ex) { _cleanupStatus!.Text = $"Temizlik başarısız: {ex.Message}"; }
    }

    private async Task CleanupScanAgainAsync()
    {
        var report = await _cleanup.ScanAsync(); _cleanupCandidates = report.Files;
        if (_cleanupList is null) return;
        _cleanupList.Items.Clear();
        foreach (var file in report.Files)
        {
            var check = new CheckBox { Tag = file, IsChecked = false, Margin = new Thickness(5, 4, 5, 4), Content = $"{file.Name}   ·   {file.SizeText}   ·   {file.LastWriteTime:g}" };
            check.Checked += (_, _) => UpdateCleanupSelection(); check.Unchecked += (_, _) => UpdateCleanupSelection();
            _cleanupList.Items.Add(check);
        }
        UpdateCleanupSelection();
    }

    private async Task<ScrollViewer> BuildHistoryPageAsync()
    {
        var stack = NewStack();
        var backups = await _optimizations.GetBackupsAsync();
        if (backups.Count == 0) stack.Children.Add(Body("Henüz kayıtlı ayar yedeği yok."));
        foreach (var backup in backups)
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = backup.DisplayText, FontSize = 14, FontWeight = FontWeights.SemiBold }); panel.Children.Add(Body($"Eylem: {backup.Backup.ActionId} · {backup.Backup.Values.Count} değer"));
            panel.Children.Add(ActionButton("Bu yedeği geri yükle…", async (_, _) => await RestoreBackupAsync(backup)));
            stack.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(21, 18, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = panel, Margin = new Thickness(0, 0, 0, 10) });
        }
        return Scroll(stack);
    }

    private async Task RestoreBackupAsync(BackupListItem backup)
    {
        if (MessageBox.Show($"'{backup.Backup.Title}' yedeği geri yüklenecek. Sonradan değiştirilmiş kayıt değerleri korunur. Devam edilsin mi?", "Geri almayı onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { MessageBox.Show(await _optimizations.RestoreAsync(backup), "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Information); await NavigateAsync("history"); }
        catch (Exception ex) { MessageBox.Show($"Geri alma başarısız: {ex.Message}", "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private ScrollViewer BuildAnalysisPage()
    {
        var stack = NewStack(); var snapshot = SystemSnapshotReader.Read();
        stack.Children.Add(Card("İŞLEMCİ", $"{snapshot.OperatingSystem} · {snapshot.Architecture}", null, snapshot.Processor));
        stack.Children.Add(Card("BELLEK", $"{snapshot.AvailableRamGb:N1} GB kullanılabilir", null, $"{snapshot.TotalRamGb:N1} GB toplam"));
        stack.Children.Add(SectionLabel("Grafik bağdaştırıcıları"));
        foreach (var gpu in snapshot.GraphicsAdapters) stack.Children.Add(Body(gpu));
        stack.Children.Add(SectionLabel("Sabit sürücüler"));
        foreach (var disk in snapshot.Disks) stack.Children.Add(Card(disk.Name, "Kullanılabilir / toplam alan", null, disk.FreeText));
        return Scroll(stack);
    }

    private StackPanel BuildAppsPage()
    {
        var stack = NewStack();
        stack.Children.Add(Body("Katalogdaki kimlikler NEXUS içinde sabit izin listesinden gelir. Paketler WinGet kaynağından indirilir; her kurulum ayrı onay gerektirir."));
        stack.Children.Add(Card("KURULUM ALTYAPISI", "WinGet / winget kaynağı", null, _catalog.IsAvailable ? "Hazır" : "WinGet bulunamadı"));
        var search = new TextBox { Text = _appsQuery, Margin = new Thickness(0, 12, 0, 10), Padding = new Thickness(10), Height = 38, ToolTip = "Uygulama veya kategori ara" };
        search.TextChanged += (_, _) => { _appsQuery = search.Text; RefreshAppsList(); };
        stack.Children.Add(search);
        _appsStatus = Body(""); stack.Children.Add(_appsStatus);
        _appsList = NewStack(); stack.Children.Add(_appsList);
        return stack;
    }

    private void RefreshAppsList()
    {
        if (_appsList is null) return;
        _appsList.Children.Clear();
        var matches = WingetCatalogService.Apps.Where(app => string.IsNullOrWhiteSpace(_appsQuery) ||
            $"{app.Name} {app.Publisher} {app.Category} {app.Description}".Contains(_appsQuery, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (_appsStatus is not null) _appsStatus.Text = _catalog.IsAvailable ? $"{matches.Length} uygulama gösteriliyor · Paket kurulumu internet bağlantısı kullanır." : "WinGet bu bilgisayarda bulunamadı; katalog listesi yalnızca bilgi amaçlıdır.";
        foreach (var app in matches)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = app.Name, FontSize = 15, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(Body($"{app.Category} · {app.Publisher} · {app.PackageId}"));
            panel.Children.Add(Body(app.Description));
            var button = ActionButton("WinGet ile kur", async (_, _) => await InstallCatalogAppAsync(app));
            button.IsEnabled = _catalog.IsAvailable;
            panel.Children.Add(button);
            _appsList.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(21, 18, 22)), BorderBrush = new SolidColorBrush(Color.FromRgb(48, 39, 46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = panel, Margin = new Thickness(0, 0, 0, 9) });
        }
    }

    private async Task InstallCatalogAppAsync(CatalogApp app)
    {
        if (MessageBox.Show($"{app.Name} ({app.Publisher}) WinGet üzerinden kurulacak.\n\nPaket kimliği: {app.PackageId}\nKaynak: winget\n\nWinGet ayrı bir pencere açabilir ve yayıncı sözleşmelerini gösterebilir. Devam edilsin mi?", "Kurulumu onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var exitCode = await _catalog.InstallAsync(app);
            MessageBox.Show(exitCode == 0 ? $"{app.Name} kurulum işlemi tamamlandı." : $"WinGet işlemi {exitCode} koduyla tamamlandı.", "NEXUS PC · WinGet", MessageBoxButton.OK, exitCode == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { MessageBox.Show($"WinGet başlatılamadı: {ex.Message}", "NEXUS PC", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private StackPanel BuildUpdatesPage()
    {
        var stack = NewStack();
        stack.Children.Add(Card("YÜKLÜ SÜRÜM", "Geliştirme sürümü", null, "0.1.0"));
        stack.Children.Add(Body("NetSparkle appcast adresi, Ed25519 doğrulama anahtarı ve imzalı yayın paketi bağlanmadı. Bu yüzden bu sürüm uzak sunucuya bağlanmaz ve güncelleme indirmez."));
        return stack;
    }

    private StackPanel BuildSettingsPage()
    {
        var stack = NewStack();
        stack.Children.Add(Card("YEREL VERİ", "Ayar yedekleri", null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUSPC", "Backups")));
        stack.Children.Add(Card("YEREL SOHBET", "Ollama konuşmaları", null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEXUSPC", "Chats")));
        stack.Children.Add(Body("Telemetri veya bulut AI aktarımı yok. Ollama ve model indirmeleri ayrı internet bağlantıları kullanır."));
        stack.Children.Add(ActionButton("Yerel AI / Model Manager", (_, _) => LocalAi_Click(this, new RoutedEventArgs())));
        return stack;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage == "home" || _currentPage == "gaming") { UpdateLiveMetrics(); if (_currentPage == "home") await CheckOllamaAsync(); }
        else if (_currentPage == "analysis") await NavigateAsync("analysis");
        else if (_currentPage == "power") await NavigateAsync("power");
    }

    private void LocalAi_Click(object sender, RoutedEventArgs e)
    { var window = new LocalAiWindow { Owner = this }; window.ShowDialog(); }
}
