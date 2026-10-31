using System.Windows;
using NexusPC.Desktop.Services;

namespace NexusPC.Desktop;

public partial class SystemAnalysisWindow : Window
{
    public SystemAnalysisWindow() { InitializeComponent(); Loaded += (_, _) => LoadSnapshot(); }

    private void LoadSnapshot()
    {
        try
        {
            var snapshot = SystemSnapshotReader.Read();
            CpuValue.Text = $"{snapshot.Processor}\n{snapshot.OperatingSystem} · {snapshot.Architecture}";
            RamValue.Text = snapshot.TotalRamGb > 0 ? $"{snapshot.TotalRamGb:N1} GB toplam" : "Bellek bilgisi alınamadı";
            RamDetail.Text = snapshot.TotalRamGb > 0 ? $"{snapshot.AvailableRamGb:N1} GB kullanılabilir" : "";
            GpuList.ItemsSource = snapshot.GraphicsAdapters.Count > 0 ? snapshot.GraphicsAdapters : new[] { "Grafik bağdaştırıcısı bilgisi alınamadı" };
            DiskList.ItemsSource = snapshot.Disks;
        }
        catch (Exception ex) { CpuValue.Text = $"Sistem bilgisi okunamadı: {ex.Message}"; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadSnapshot();
}
