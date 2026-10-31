using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TaskNova.App.Views;

/// <summary>Read-only text viewer.</summary>
public partial class TextWindow : Window
{
    public TextWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this);
    }

    public TextWindow(string title, string text) : this()
    {
        Title = title;
        HeaderText.Text = title;
        Body.Text = text;
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(Body.Text ?? "");
            }
            catch (Exception)
            {
                // Clipboard held by another program.
            }
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
