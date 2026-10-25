using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Otimizador.App.Overlay;
using Otimizador.App.ViewModels;

namespace Otimizador.App;

/// <summary>
/// Gera capturas PNG da própria interface (todas as páginas, nos dois temas):
/// <c>Otimizador.exe --snapshots pasta</c>. Serve para revisar o visual sem depender de captura de tela do Windows.
/// </summary>
public static class SnapshotTool
{
    private static readonly string[] Pages = { "Dashboard", "Hardware", "Monitor", "Cleanup", "Startup", "Test", "Optimize", "History", "About" };

    /// <summary>Primeiro ScrollViewer com conteúdo maior que a área visível (a rolagem da página aberta).</summary>
    private static System.Windows.Controls.ScrollViewer? FindScroller(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }

    public static async Task RunAsync(MainWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        var vm = (MainViewModel)window.DataContext;
        window.Left = 40;
        window.Top = 40;
        Loc.Instance.SetLanguage("pt");

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeService.Apply(theme, save: false);
            foreach (var page in Pages)
            {
                vm.OpenCommand.Execute(page);
                await Task.Delay(page is "Hardware" or "Cleanup" ? 7000 : 3200);
                Save(window, Path.Combine(dir, $"{theme}-{page}.png"));

                // Meio e parte de baixo da página (rolada).
                if (FindScrollViewer(window) is { } sv)
                {
                    sv.ScrollToVerticalOffset(sv.ScrollableHeight * 0.3);
                    await Task.Delay(400);
                    Save(window, Path.Combine(dir, $"{theme}-{page}-meio.png"));
                    sv.ScrollToEnd();
                    await Task.Delay(500);
                    Save(window, Path.Combine(dir, $"{theme}-{page}-fim.png"));
                }
            }

            // Estados com ação: teste rápido concluído e perfil Seguro marcado.
            vm.OpenCommand.Execute("Test");
            await Task.Delay(600);
            if (vm.CurrentPage is TestViewModel test)
            {
                test.LoadDemo(); // resultados de exemplo (rodar os testes de verdade levaria mais de 20 s)
                await Task.Delay(1500);
                Save(window, Path.Combine(dir, $"{theme}-Test-done.png"));

                // card de renderização: rola a página até o fim
                var scroller = FindScroller(window);
                if (scroller is not null)
                {
                    scroller.ScrollToEnd();
                    await Task.Delay(600);
                    Save(window, Path.Combine(dir, $"{theme}-Test-render.png"));
                    scroller.ScrollToTop();
                }
            }

            vm.OpenCommand.Execute("Optimize:safe");
            await Task.Delay(2500);
            Save(window, Path.Combine(dir, $"{theme}-Optimize-safe.png"));
        }

        // Páginas novas: Ajustes, Inventário (com um PC publicado numa pasta temporária) e Segurança.
        var invDir = Path.Combine(Path.GetTempPath(), "otimizador-snap-inv");
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            ThemeService.Apply(theme, save: false);

            vm.OpenCommand.Execute("Settings");
            await Task.Delay(2500);
            Save(window, Path.Combine(dir, $"{theme}-Settings.png"));
            if (FindScrollViewer(window) is { } sv0) { sv0.ScrollToEnd(); await Task.Delay(400); Save(window, Path.Combine(dir, $"{theme}-Settings-fim.png")); }

            vm.OpenCommand.Execute("Inventory");
            await Task.Delay(800);
            if (vm.CurrentPage is InventoryViewModel inv)
            {
                inv.Folder = invDir;
                inv.HasFolder = true;
                inv.PublishCommand.Execute(null);
                await Task.Delay(9000);
                Save(window, Path.Combine(dir, $"{theme}-Inventory.png"));
            }

            vm.OpenCommand.Execute("Security");
            await Task.Delay(theme == AppTheme.Light ? 35000 : 2500);
            Save(window, Path.Combine(dir, $"{theme}-Security.png"));
        }
        try { Directory.Delete(invDir, true); } catch { /* limpeza opcional */ }

        // Espaço em disco: varredura real do disco do sistema, nos dois temas.
        ThemeService.Apply(AppTheme.Light, save: false);
        vm.OpenCommand.Execute("Space");
        await Task.Delay(1500);
        Save(window, Path.Combine(dir, "Light-Space.png"));
        if (vm.CurrentPage is SpaceViewModel space)
        {
            space.ScanCommand.Execute(null);
            await Task.Delay(8000);
            Save(window, Path.Combine(dir, "Light-Space-scanning.png"));
            var sw = Stopwatch.StartNew();
            while (!space.HasResult && sw.Elapsed < TimeSpan.FromMinutes(6)) await Task.Delay(1000);
            File.WriteAllText(Path.Combine(dir, "space-scan-seconds.txt"), $"{sw.Elapsed.TotalSeconds:0.0} s; HasResult={space.HasResult}");
            await Task.Delay(1000);
            Save(window, Path.Combine(dir, "Light-Space-result.png"));
            if (FindScrollViewer(window) is { } svs)
            {
                svs.ScrollToEnd();
                await Task.Delay(500);
                Save(window, Path.Combine(dir, "Light-Space-result-fim.png"));
                svs.ScrollToTop();
            }
            // Duplicados (comparação real do conteúdo dos arquivos grandes)
            space.SelectedMode = space.ListModes[2];
            space.FindDuplicatesCommand.Execute(null);
            var dsw = Stopwatch.StartNew();
            await Task.Delay(1500);
            while (space.IsFindingDuplicates && dsw.Elapsed < TimeSpan.FromMinutes(3)) await Task.Delay(1000);
            File.WriteAllText(Path.Combine(dir, "dups-seconds.txt"), $"{dsw.Elapsed.TotalSeconds:0.0} s; grupos={space.Duplicates.Count}; {space.DuplicateStatus}");
            await Task.Delay(800);
            if (FindScrollViewer(window) is { } svd)
            {
                svd.ScrollToEnd();
                await Task.Delay(500);
                Save(window, Path.Combine(dir, "Light-Space-duplicados.png"));
                svd.ScrollToTop();
            }
            space.SelectedMode = space.ListModes[0];

            ThemeService.Apply(AppTheme.Dark, save: false);
            await Task.Delay(900);
            Save(window, Path.Combine(dir, "Dark-Space-result.png"));
        }

        // Overlay de métricas (sempre escuro, em qualquer tema).
        OverlayService.Instance.ShowDisk = true;
        OverlayService.Instance.IsVisible = true;
        await Task.Delay(2500);
        if (OverlayService.Instance.Window is { } ow) Save(ow, Path.Combine(dir, "Overlay.png"));
        OverlayService.Instance.IsVisible = false;

        // Aviso de alerta
        Alerts.AlertService.Instance.TestCommand.Execute(null);
        await Task.Delay(1500);
        if (Alerts.ToastWindow.Current is { } toast) Save(toast, Path.Combine(dir, "Toast.png"));

        Loc.Instance.SetLanguage("en");
        vm.OpenCommand.Execute("Dashboard");
        await Task.Delay(3000);
        Save(window, Path.Combine(dir, "Dark-Dashboard-en.png"));

        File.WriteAllText(Path.Combine(dir, "tray.txt"), $"ativo={Tray.TrayService.IsActive}; criado={Tray.TrayService.IsCreated}; erro={Tray.TrayService.LastError}");
        Application.Current.Shutdown();
    }

    /// <summary>Primeiro ScrollViewer da página atual (o que rola o conteúdo principal).</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    private static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;
        int w = (int)Math.Ceiling(root.ActualWidth), h = (int)Math.Ceiling(root.ActualHeight);
        if (w <= 0 || h <= 0) return;

        var bg = Application.Current.TryFindResource("AppBg") as Brush ?? Brushes.White;
        var backdrop = new DrawingVisual();
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(backdrop);
        bmp.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}









