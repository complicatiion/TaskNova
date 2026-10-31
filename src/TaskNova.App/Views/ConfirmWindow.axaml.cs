using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Views;

/// <summary>Confirmation dialog with optional switch or text input. Main action right, Cancel left.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this);
    }

    public ConfirmWindow(string title, string message, string confirmText, bool danger, string? optionText = null, bool optionDefault = false,
        string? confirmIcon = null) : this()
    {
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        ConfirmLabel.Text = confirmText;
        ConfirmLabel.Icon = Res.Icon(confirmIcon ?? (danger ? "IconDelete" : "IconCheck"));
        Glyph.Data = Res.Icon(danger ? "IconAlert" : "IconInfo");
        Glyph.Foreground = Res.Brush(danger ? "BrushDeep" : "BrushAccentText");
        if (danger)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");

            // Destructive: Enter does not confirm by accident.
            ConfirmButton.IsDefault = false;
        }

        if (!string.IsNullOrEmpty(optionText))
        {
            OptionText.Text = optionText;
            OptionCheck.IsChecked = optionDefault;
            OptionRow.IsVisible = true;
        }
    }

    /// <summary>Dialog with one text field (e.g. a message to a session).</summary>
    public ConfirmWindow(string title, string message, string confirmText, string watermark, string? initial, bool multiline)
        : this(title, message, confirmText, danger: false, confirmIcon: "IconMessageText")
    {
        InputBox.Watermark = watermark;
        InputBox.Text = initial;
        InputBox.IsVisible = true;
        if (multiline)
        {
            InputBox.AcceptsReturn = true;
            InputBox.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            InputBox.Height = 110;
            ConfirmButton.IsDefault = false;
        }

        Opened += (_, _) => InputBox.Focus();
    }

    public string Input => InputBox.Text?.Trim() ?? "";

    public bool OptionChecked => OptionCheck.IsChecked == true;

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
