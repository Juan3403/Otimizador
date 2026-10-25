using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Otimizador.App;

/// <summary>Texto traduzível que se atualiza sozinho quando o idioma muda (uso em itens de lista/menu).</summary>
public sealed class LocText : ObservableObject
{
    public string Key { get; }

    public string Text => Loc.T(Key);

    public LocText(string key)
    {
        Key = key;
        PropertyChangedEventManager.AddHandler(Loc.Instance, OnLocChanged, string.Empty);
    }

    private void OnLocChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(Text));

    public override string ToString() => Text;
}

