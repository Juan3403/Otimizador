using System.Windows;

namespace Otimizador.App.Dialogs;

/// <summary>Caixa de confirmação com o tema do app (no lugar do MessageBox, que ignora o tema escuro).</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string? details, string okText, string? cancelText, string glyph)
    {
        InitializeComponent();
        ThemeService.Attach(this);
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = glyph;
        OkButton.Content = okText;
        if (cancelText is null) CancelButton.Visibility = Visibility.Collapsed;
        else CancelButton.Content = cancelText;
        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsText.Text = details;
            DetailsCard.Visibility = Visibility.Visible;
        }
        var main = Application.Current.MainWindow;
        if (main is { IsLoaded: true } && !ReferenceEquals(main, this)) Owner = main;
    }

    public static bool Confirm(string title, string message, string? details = null, string? okText = null) =>
        new ConfirmDialog(title, message, details, okText ?? Loc.T("Dlg_Ok"), Loc.T("Dlg_Cancel"), "").ShowDialog() == true;

    public static void Info(string title, string message, string? details = null) =>
        new ConfirmDialog(title, message, details, Loc.T("Dlg_Ok"), null, "").ShowDialog();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
