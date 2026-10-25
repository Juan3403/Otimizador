using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace Otimizador.App;

/// <summary>Tradução PT-BR/EN com troca em tempo de execução.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt" : "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Lang => _lang;

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string T(string pt, string en) => Instance._lang == "pt" ? pt : en;

    public void Toggle() => SetLanguage(_lang == "pt" ? "en" : "pt");

    public void SetLanguage(string lang)
    {
        if (lang == _lang) return;
        _lang = lang;
        ApplyCulture();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
    }

    /// <summary>Números e datas seguem o idioma escolhido (vírgula em PT-BR, ponto em EN).</summary>
    public void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(_lang == "pt" ? "pt-BR" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private string Get(string key) =>
        Strings.Table.TryGetValue(key, out var v) ? (_lang == "pt" ? v.Pt : v.En) : key;
}

/// <summary>Uso em XAML: Text="{app:Tr Home}".</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; }

    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

