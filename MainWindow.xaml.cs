using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Otimizador.App.GameMode;
using Otimizador.App.Overlay;
using Otimizador.App.Tray;

namespace Otimizador.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xA11;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    private const uint VK_O = 0x4F;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        ThemeService.Attach(this);
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
            // Ctrl+Alt+O liga/desliga o overlay de qualquer lugar (se outro programa já usa o atalho, só não funciona)
            RegisterHotKey(source?.Handle ?? IntPtr.Zero, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_O);
        };
        Closing += (_, e) =>
        {
            // com "fechar para a bandeja" ligado, o X só esconde a janela (o overlay, os alertas e a limpeza agendada continuam)
            if (!TrayService.Exiting && TrayService.IsActive && Settings.Load().CloseToTray)
            {
                e.Cancel = true;
                Hide();
            }
        };
        Closed += (_, _) =>
        {
            GameModeService.Instance.Shutdown();
            TrayService.Dispose();
            UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
            OverlayService.Instance.IsVisible = false;
        };
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            OverlayService.Instance.Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }
}

