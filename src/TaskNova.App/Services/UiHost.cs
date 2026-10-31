using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using TaskNova.App.Views;

namespace TaskNova.App.Services;

public readonly record struct ConfirmResult(bool Confirmed, bool OptionChecked);

/// <summary>Windows, dialogs and clipboard for the view models.</summary>
public sealed class UiHost
{
    public Window? MainWindow { get; set; }

    public Action? ShowMainWindow { get; set; }

    public Action? ExitApplication { get; set; }

    public nint MainHandle => WindowChrome.Handle(MainWindow);

    public async Task CopyAsync(string? text)
    {
        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(MainWindow)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(text);
            }
            catch (Exception)
            {
                // Clipboard held by another program.
            }
        }
    }

    /// <summary>Confirmation dialog: main action right, Cancel left. Danger = red button, Enter does not confirm.</summary>
    public async Task<ConfirmResult> ConfirmAsync(string title, string message, string confirmText, bool danger, string? optionText = null,
        bool optionDefault = false, string? confirmIcon = null)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return default;
        }

        var dialog = new ConfirmWindow(title, message, confirmText, danger, optionText, optionDefault, confirmIcon);
        var confirmed = await dialog.ShowDialog<bool>(window);
        return new ConfirmResult(confirmed, confirmed && dialog.OptionChecked);
    }

    /// <summary>Asks for one text (session message); null = cancelled.</summary>
    public async Task<string?> PromptAsync(string title, string message, string confirmText, string watermark, string? initial = null,
        bool multiline = false)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new ConfirmWindow(title, message, confirmText, watermark, initial, multiline);
        return await dialog.ShowDialog<bool>(window) ? dialog.Input : null;
    }

    public async Task ShowTextAsync(string title, string text)
    {
        var window = EnsureWindow();
        if (window is not null)
        {
            await new TextWindow(title, text).ShowDialog(window);
        }
    }

    /// <summary>CPU affinity dialog; null = cancelled.</summary>
    public async Task<ulong?> AffinityAsync(string processName, ulong current, ulong system, int processors)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new AffinityWindow(processName, current, system, processors);
        return await dialog.ShowDialog<bool>(window) ? dialog.Mask : null;
    }

    private readonly Dictionary<TaskNova.Core.Models.ProcessKey, ProcessWindow> _processWindows = new();

    /// <summary>Process window: one per process; a second call brings the open one to the front.</summary>
    public void ShowProcessWindow(ViewModels.MainViewModel main, ViewModels.ProcessRow row)
    {
        if (_processWindows.TryGetValue(row.Key, out var open))
        {
            open.Activate();
            return;
        }

        var window = new ProcessWindow { DataContext = new ViewModels.ProcessWindowViewModel(main, row) };
        _processWindows[row.Key] = window;
        window.Closed += (_, _) => _processWindows.Remove(row.Key);
        if (MainWindow is { IsVisible: true } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>Run new task dialog; null = cancelled.</summary>
    public async Task<(string Command, bool Elevated)?> RunTaskAsync(bool isElevated, IReadOnlyList<string> history)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new RunTaskWindow(isElevated, history);
        return await dialog.ShowDialog<bool>(window) ? (dialog.Command, dialog.Elevated) : null;
    }

    /// <summary>Save dialog; returns the local path or null.</summary>
    public async Task<string?> SaveFileAsync(string title, string suggestedName, string typeName, string extension)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }]
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Open dialog for one file or one folder; returns the local path or null.</summary>
    public async Task<string?> PickPathAsync(string title, bool folder)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        if (folder)
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private Window? EnsureWindow()
    {
        if (MainWindow is { IsVisible: false })
        {
            ShowMainWindow?.Invoke();
        }

        // An open dialog owns nested dialogs.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = desktop.Windows.LastOrDefault(w => w.IsVisible && w.IsActive && w != MainWindow && w is not MiniMonitorWindow);
            if (dialog is not null)
            {
                return dialog;
            }
        }

        return MainWindow;
    }
}
