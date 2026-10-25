using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Otimizador.App;

public enum AppTheme { Light, Dark }

/// <summary>Troca o tema (claro/escuro) em tempo real e ajusta a barra de título do Windows.</summary>
public static class ThemeService
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static event Action? Changed;

    /// <summary>Lê as preferências salvas (ou o tema do Windows) e aplica antes de abrir a janela.</summary>
    public static void Init()
    {
        var s = Settings.Load();
        if (s.Language is "pt" or "en") Loc.Instance.SetLanguage(s.Language);
        Loc.Instance.ApplyCulture();
        var theme = s.Theme switch
        {
            "dark" => AppTheme.Dark,
            "light" => AppTheme.Light,
            _ => SystemTheme(),
        };
        Apply(theme, save: false);
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, save: true);

    public static void SaveLanguage()
    {
        var s = Settings.Load();
        s.Language = Loc.Instance.Lang;
        s.Save();
    }

    public static void Apply(AppTheme theme, bool save)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };
        if (dicts.Count > 0) dicts[0] = dict;
        else dicts.Add(dict);

        Current = theme;
        if (save)
        {
            var s = Settings.Load();
            s.Theme = theme == AppTheme.Dark ? "dark" : "light";
            s.Save();
        }
        foreach (Window w in Application.Current.Windows) ApplyToWindow(w);
        Changed?.Invoke();
    }

    private static AppTheme SystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch
        {
            return AppTheme.Light;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Barra de título escura/clara com a cor do tema (Windows 11; ignorado onde não houver suporte).</summary>
    public static void ApplyToWindow(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = Current == AppTheme.Dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            int square = 1; // DWMWCP_DONOTROUND: cantos retos
            DwmSetWindowAttribute(hwnd, 33, ref square, sizeof(int));

            if (Application.Current.TryFindResource("AppBgColor") is Color c)
            {
                int caption = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                int text = Current == AppTheme.Dark ? 0xF5EDE9 : 0x2A170F;
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
            }
        }
        catch
        {
            // sem suporte: mantém a barra padrão
        }
    }

    /// <summary>Chame no construtor de cada janela.</summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyToWindow(window);
    }
}


