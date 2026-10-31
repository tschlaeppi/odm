using System.Diagnostics;
using System.Text;

namespace Odm.Agent.Os;

/// <summary>Starts external programs without a shell (arguments are passed individually, no injection).</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Output, string Error, bool TimedOut);

    public static async Task<Result> RunAsync(
        string file,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        var output = new TailBuffer(1_000_000);
        var error = new TailBuffer(100_000);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => output.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => error.AppendLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // process has already exited
            }

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            return new Result(-1, output.ToString(), error.ToString(), TimedOut: true);
        }

        process.WaitForExit(); // makes sure the last output has been read
        return new Result(process.ExitCode, output.ToString(), error.ToString(), TimedOut: false);
    }

    /// <summary>Keeps only the end of the output; that is where the relevant lines are on errors.</summary>
    private sealed class TailBuffer(int capacity)
    {
        private readonly StringBuilder _text = new();
        private readonly object _gate = new();

        public void AppendLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _text.AppendLine(line);
                if (_text.Length > capacity)
                {
                    _text.Remove(0, _text.Length - capacity);
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }
}
