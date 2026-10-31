using System.Diagnostics;
using System.Text;

namespace TaskNova.Core.Diagnostics;

/// <summary>Runs a helper tool once (schtasks for the own sign-in task, explorer) and returns exit code and output.</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Output, bool TimedOut)
    {
        public bool Success => ExitCode == 0 && !TimedOut;
    }

    public static Result Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var output = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                return new Result(-1, "", false);
            }
        }
        catch (Exception ex)
        {
            return new Result(-1, ex.Message, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return new Result(-1, output.ToString(), true);
        }

        process.WaitForExit();
        lock (output)
        {
            return new Result(process.ExitCode, output.ToString(), false);
        }
    }
}
