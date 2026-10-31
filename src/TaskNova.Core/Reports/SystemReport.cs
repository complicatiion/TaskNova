using System.Net;
using System.Text;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;

namespace TaskNova.Core.Reports;

/// <summary>One process line of the report.</summary>
public sealed record ReportProcess(string Name, int Pid, string? User, double Cpu, ulong Memory, int Threads, int Handles, string? Path);

/// <summary>
/// Self-contained HTML system report (no external CSS, fonts or scripts – opens offline in any browser):
/// system facts, current load, top processes, services and sessions.
/// </summary>
public static class SystemReport
{
    public static string Build(StaticSystemInfo info, SystemSnapshot? snapshot, IReadOnlyList<ReportProcess> processes,
        IReadOnlyList<ServiceInfo> services, IReadOnlyList<SessionInfo> sessions, string version, DateTime createdLocal)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>TaskNova report</title>
            <style>
            body{font-family:Segoe UI,system-ui,sans-serif;background:#0B1026;color:#F8F9FF;margin:32px}
            h1{color:#EF476F;margin:0 0 4px}h2{color:#EF476F;margin:28px 0 8px;font-size:18px}
            .muted{color:#A9AEC8}table{border-collapse:collapse;width:100%;font-size:13px}
            th,td{padding:6px 10px;border-bottom:1px solid #2B2D42;text-align:left;vertical-align:top}
            th{color:#A9AEC8;font-weight:600}td.n{text-align:right;font-variant-numeric:tabular-nums}
            .grid{display:grid;grid-template-columns:220px 1fr;gap:4px 16px;font-size:14px}
            @media print{body{background:#fff;color:#000}h1,h2{color:#B5173F}.muted,th{color:#444}}
            </style></head><body>
            """);
        html.Append($"<h1>TaskNova report</h1><div class=\"muted\">{E(info.ComputerName)} · {E(createdLocal.ToString("g", Fmt.Culture))} · TaskNova {E(version)}</div>");

        html.Append("<h2>System</h2><div class=\"grid\">");
        void Row(string label, string? value) => html.Append($"<div class=\"muted\">{E(label)}</div><div>{E(value)}</div>");
        Row("Windows", $"{info.OsName} {info.OsVersion}");
        Row("Type", (info.IsServer ? "Server" : "Workstation") + (info.VirtualMachine ? " · virtual machine" : ""));
        Row("CPU", info.CpuName);
        Row("Cores", $"{info.CpuSockets} socket(s) · {info.CpuCores} cores · {info.LogicalProcessors} logical");
        Row("Memory", Fmt.Bytes(info.TotalMemoryBytes));
        Row("GPU", string.Join(", ", info.GpuNames));
        Row("User", info.UserName + (info.IsElevated ? " (administrator)" : ""));
        if (snapshot is not null)
        {
            Row("Up time", Fmt.Uptime(snapshot.Uptime));
            Row("CPU usage", Fmt.Percent(snapshot.Cpu.TotalPercent));
            Row("Memory in use", $"{Fmt.Bytes(snapshot.Memory.UsedBytes)} / {Fmt.Bytes(snapshot.Memory.TotalBytes)}");
            Row("Committed", $"{Fmt.Bytes(snapshot.Memory.CommitBytes)} / {Fmt.Bytes(snapshot.Memory.CommitLimitBytes)}");
            Row("Processes · threads · handles", $"{snapshot.ProcessCount} · {snapshot.ThreadCount} · {snapshot.HandleCount}");
            foreach (var disk in snapshot.Disks)
            {
                Row("Disk " + disk.Name, $"{Fmt.Percent(disk.ActivePercent)} active · read {Fmt.BytesPerSecond(disk.ReadBytesPerSec)} · write {Fmt.BytesPerSecond(disk.WriteBytesPerSec)}");
            }

            foreach (var network in snapshot.Networks)
            {
                Row(network.Name, $"{network.Description} · {network.IPv4} · {Fmt.LinkSpeed(network.LinkSpeedBitsPerSec)}");
            }
        }

        html.Append("</div>");

        void Processes(string title, IEnumerable<ReportProcess> list)
        {
            html.Append($"<h2>{E(title)}</h2><table><tr><th>Name</th><th>PID</th><th>User</th><th>CPU</th><th>Memory</th><th>Threads</th><th>Handles</th><th>Path</th></tr>");
            foreach (var p in list)
            {
                html.Append($"<tr><td>{E(p.Name)}</td><td class=\"n\">{p.Pid}</td><td>{E(p.User)}</td><td class=\"n\">{E(Fmt.CpuPercent(p.Cpu))}</td>" +
                            $"<td class=\"n\">{E(Fmt.MemoryMb(p.Memory))}</td><td class=\"n\">{p.Threads}</td><td class=\"n\">{p.Handles}</td><td>{E(p.Path)}</td></tr>");
            }

            html.Append("</table>");
        }

        Processes("Top processes by CPU", processes.OrderByDescending(p => p.Cpu).Take(15));
        Processes("Top processes by memory", processes.OrderByDescending(p => p.Memory).Take(15));

        if (sessions.Count > 0)
        {
            html.Append("<h2>Sessions</h2><table><tr><th>ID</th><th>User</th><th>State</th><th>Protocol</th><th>Client</th><th>Signed in</th></tr>");
            foreach (var s in sessions)
            {
                html.Append($"<tr><td class=\"n\">{s.SessionId}</td><td>{E(s.FullUserName)}</td><td>{s.State}</td><td>{s.Protocol}</td>" +
                            $"<td>{E(s.ClientName)} {E(s.ClientAddress)}</td><td>{E(Fmt.Date(s.LogonTimeUtc))}</td></tr>");
            }

            html.Append("</table>");
        }

        if (services.Count > 0)
        {
            html.Append("<h2>Running services</h2><table><tr><th>Name</th><th>Display name</th><th>PID</th><th>Start type</th></tr>");
            foreach (var s in services.Where(s => s.State == ServiceState.Running).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                html.Append($"<tr><td>{E(s.Name)}</td><td>{E(s.DisplayName)}</td><td class=\"n\">{s.ProcessId}</td><td>{s.StartMode}</td></tr>");
            }

            html.Append("</table>");
        }

        html.Append("<p class=\"muted\">Created locally by TaskNova – nothing was sent anywhere.</p></body></html>");
        return html.ToString();
    }
}
