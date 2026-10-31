using System.Windows;
using System.Windows.Controls;
using NexusPC.Desktop.Services;

namespace NexusPC.Desktop;

public partial class OptimizationsWindow : Window
{
    private readonly OptimizationService _service = new();

    public OptimizationsWindow()
    {
        InitializeComponent();
        OptionsList.ItemsSource = OptimizationService.Catalog;
        Loaded += async (_, _) => await LoadBackupsAsync();
    }

    private async Task LoadBackupsAsync()
    {
        try { BackupsList.ItemsSource = await _service.GetBackupsAsync(); }
        catch (Exception ex) { StatusText.Text = $"Yedek listesi okunamadı: {ex.Message}"; }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var option = OptimizationService.Catalog.SingleOrDefault(x => x.Id == id);
        if (option is null) { StatusText.Text = "İşlem izin listesinde bulunamadı."; return; }
        var settings = string.Join("\n• ", option.AffectedSettings);
        var answer = MessageBox.Show($"'{option.Title}' uygulanacak.\n\nEtkilenecek ayarlar:\n• {settings}\n\nMevcut değerler önce NEXUSPC\\Backups klasörüne kaydedilecek. İşlemi onaylıyor musun?", "Değişikliği önizle", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            StatusText.Text = await _service.ApplyAsync(id);
            await LoadBackupsAsync();
        }
        catch (Exception ex) { StatusText.Text = $"İşlem uygulanamadı: {ex.Message}"; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupListItem backup) { StatusText.Text = "Önce bir geri alma kaydı seç."; return; }
        if (MessageBox.Show($"'{backup.Backup.Title}' yedeğindeki değerler geri yüklenecek. Bu işlemden sonra ayarlar yeniden değişebilir. Devam edilsin mi?", "Geri almayı onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { StatusText.Text = await _service.RestoreAsync(backup); }
        catch (Exception ex) { StatusText.Text = $"Geri alma başarısız: {ex.Message}"; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadBackupsAsync();
}
