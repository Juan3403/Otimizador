using CommunityToolkit.Mvvm.ComponentModel;

namespace Otimizador.App.ViewModels;

public abstract class PageViewModel : ObservableObject, IDisposable
{
    public abstract string TitleKey { get; }

    public abstract string SubtitleKey { get; }

    public virtual void Dispose()
    {
    }
}
