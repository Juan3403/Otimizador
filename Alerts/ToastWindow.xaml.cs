using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Otimizador.App.Alerts;

public partial class ToastWindow : Window
{
    private static ToastWindow? _current;

    /// <summary>Aviso atualmente na tela (usado pelas capturas de teste).</summary>
    public static Window? Current => _current;

    private ToastWindow(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        MouseLeftButtonUp += (_, _) => Close();

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsLoaded) Close();
        };
        timer.Start();
    }

    /// <summary>Mostra um aviso no canto inferior direito (substitui o anterior).</summary>
    public static void Show(string title, string message)
    {
        void Open()
        {
            _current?.Close();
            _current = new ToastWindow(title, message);
            _current.Closed += (_, _) => _current = null;
            _current.Show();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) Open();
        else dispatcher.BeginInvoke(Open);
    }
}

