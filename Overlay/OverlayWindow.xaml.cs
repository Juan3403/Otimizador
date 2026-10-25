using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Otimizador.App.Overlay;

public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    public OverlayWindow()
    {
        DataContext = OverlayService.Instance;
        InitializeComponent();

        var s = Settings.Load();
        double x = s.OverlayX ?? 24, y = s.OverlayY ?? 24;
        // se o monitor onde ficava sumiu, volta para o canto da tela principal
        if (x < SystemParameters.VirtualScreenLeft - 20 || x > SystemParameters.VirtualScreenWidth + SystemParameters.VirtualScreenLeft - 40) x = 24;
        if (y < SystemParameters.VirtualScreenTop - 20 || y > SystemParameters.VirtualScreenHeight + SystemParameters.VirtualScreenTop - 40) y = 24;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = x;
        Top = y;

        SourceInitialized += (_, _) => ApplyLock();
        MouseLeftButtonDown += OnMouseDown;
    }

    /// <summary>Travado: o clique atravessa o overlay (útil em jogos). Destravado: dá para arrastar.</summary>
    public void ApplyLock()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        style = OverlayService.Instance.IsLocked ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (OverlayService.Instance.IsLocked || e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            OverlayService.Instance.SavePosition(Left, Top);
        }
        catch (InvalidOperationException)
        {
            // soltou o botão antes de iniciar o arrasto
        }
    }
}
