using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TaskNova.App.Localization;

namespace TaskNova.App.Views;

/// <summary>"Run new task" (like Win+R / Task Manager › Run new task): program, document, folder or URL, optionally elevated.</summary>
public partial class RunTaskWindow : Window
{
    public RunTaskWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this);
    }

    public RunTaskWindow(bool isElevated, IReadOnlyList<string> history) : this()
    {
        CommandBox.ItemsSource = history;
        CommandBox.Text = history.FirstOrDefault() ?? "";
        if (isElevated)
        {
            // TaskNova already runs as administrator: new tasks inherit it.
            ElevatedSwitch.IsChecked = true;
            ElevatedSwitch.IsEnabled = false;
            ElevatedText.Text = Loc.T("RunAlreadyElevated");
        }

        Opened += (_, _) =>
        {
            CommandBox.Focus();
            if (CommandBox.FindDescendantTextBox() is { } box)
            {
                box.SelectAll();
            }
        };
    }

    public string Command => CommandBox.Text?.Trim() ?? "";

    public bool Elevated => ElevatedSwitch.IsChecked == true && ElevatedSwitch.IsEnabled;

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("RunTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("FileTypePrograms")) { Patterns = ["*.exe", "*.com", "*.bat", "*.cmd", "*.ps1", "*.msc", "*.lnk"] },
                new FilePickerFileType(Loc.T("FileTypeAll")) { Patterns = ["*"] }
            ]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            CommandBox.Text = path.Contains(' ') ? $"\"{path}\"" : path;
        }
    }

    private void OnRun(object? sender, RoutedEventArgs e)
    {
        if (Command.Length > 0)
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

internal static class AutoCompleteBoxExtensions
{
    public static TextBox? FindDescendantTextBox(this AutoCompleteBox box) =>
        Avalonia.VisualTree.VisualExtensions.FindDescendantOfType<TextBox>(box);
}
