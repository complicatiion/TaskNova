using Avalonia.Threading;
using TaskNova.App.Localization;
using TaskNova.App.ViewModels;
using TaskNova.App.Views;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;

namespace TaskNova.App.Services;

/// <summary>
/// Threshold alerts (off by default): CPU or memory above the limit for a while shows a small notification window in the
/// corner of the primary screen – also while TaskNova sits in the tray. One alert per kind per cooldown.
/// </summary>
public sealed class AlertService(MainViewModel main)
{
    private DateTime? _cpuSince;
    private DateTime? _memorySince;
    private DateTime _cpuLast = DateTime.MinValue;
    private DateTime _memoryLast = DateTime.MinValue;
    private AlertWindow? _window;

    public void OnSnapshot(SystemSnapshot s)
    {
        var settings = main.Settings;
        if (!settings.AlertsEnabled)
        {
            _cpuSince = _memorySince = null;
            return;
        }

        var now = DateTime.UtcNow;
        Check(s.Cpu.TotalPercent >= settings.AlertCpuPercent, ref _cpuSince, ref _cpuLast, now, () =>
            Show("cpu", Loc.T("AlertCpuTitle"), Loc.F("AlertCpuText", Fmt.Percent(s.Cpu.TotalPercent), settings.AlertSeconds) + TopText(cpu: true)));
        Check(s.Memory.UsedPercent >= settings.AlertMemoryPercent, ref _memorySince, ref _memoryLast, now, () =>
            Show("memory", Loc.T("AlertMemoryTitle"),
                Loc.F("AlertMemoryText", Fmt.Percent(s.Memory.UsedPercent), Fmt.Bytes(s.Memory.UsedBytes), Fmt.Bytes(s.Memory.TotalBytes)) + TopText(cpu: false)));
    }

    private void Check(bool over, ref DateTime? since, ref DateTime last, DateTime now, Action raise)
    {
        if (!over)
        {
            since = null;
            return;
        }

        since ??= now;
        var settings = main.Settings;
        if ((now - since.Value).TotalSeconds >= settings.AlertSeconds && (now - last).TotalMinutes >= settings.AlertCooldownMinutes)
        {
            last = now;
            raise();
        }
    }

    /// <summary>The biggest consumer, when the process list is sampled at that moment.</summary>
    private string TopText(bool cpu)
    {
        var rows = main.Processes.Rows.Where(r => r.Pid != 0).ToList();
        if (rows.Count == 0)
        {
            return "";
        }

        var top = cpu ? rows.MaxBy(r => r.Cpu)! : rows.MaxBy(r => r.Memory)!;
        return "\n" + Loc.F("AlertTop", top.DisplayName, cpu ? top.CpuText : top.MemoryText);
    }

    public void ShowTest() => Show("cpu", Loc.T("AlertTestTitle"), Loc.T("AlertTestText"));

    private void Show(string kind, string title, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _window?.Close();
            var window = new AlertWindow(title, text, kind == "memory" ? "IconMemory" : "IconChip");
            _window = window;
            window.OpenRequested += () =>
            {
                main.Host.ShowMainWindow?.Invoke();
                main.SearchOn(main.ProcessesPage, "");
                main.ProcessesPage.SortBy(kind == "memory" ? "memory" : "cpu");
            };
            // Only forget the window that closed – a replaced one may report Closed after its successor is open.
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };
            window.Show();
        });
    }
}
