using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using TradingManager.Options;

namespace TradingManager.Services;

public sealed class TradingAgentsProcessService : IDisposable
{
    private readonly object _sync = new();
    private readonly ManagerOptions _options;
    private Process? _process;
    private DateTimeOffset? _startedAt;
    private readonly List<string> _logLines = [];

    public TradingAgentsProcessService(IOptions<ManagerOptions> options)
    {
        _options = options.Value;
    }

    public async Task<(bool IsRunning, int? Pid, DateTimeOffset? StartedAt)> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        Process? proc;
        lock (_sync)
        {
            proc = _process;
        }

        if (proc is { HasExited: false })
        {
            return (true, proc.Id, _startedAt);
        }

        var reachable = await IsApiReachableAsync(cancellationToken);
        if (reachable)
        {
            // API is alive but not started by this manager process (or manager restarted).
            return (true, null, null);
        }

        return (false, null, null);
    }

    public string GetBaseUrl() => $"http://{_options.TradingAgentsApiHost}:{_options.TradingAgentsApiPort}";

    public IReadOnlyList<string> GetLogs(int tail = 200)
    {
        lock (_sync)
        {
            return _logLines.TakeLast(Math.Max(1, tail)).ToList();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (await IsApiReachableAsync(cancellationToken))
        {
            AppendLog("TradingAgents API already reachable on configured host/port; skipping new start.");
            return;
        }

        lock (_sync)
        {
            if (_process is { HasExited: false })
            {
                return;
            }
        }

        var repoPath = ResolveRepoPath();
        var (fileName, arguments) = BuildStartCommand();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["TRADINGAGENTS_API_HOST"] = _options.TradingAgentsApiHost;
        psi.Environment["TRADINGAGENTS_API_PORT"] = _options.TradingAgentsApiPort.ToString();

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        process.Exited += (_, _) => AppendLog("TradingAgents API process exited.");

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start TradingAgents API process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_sync)
        {
            _process = process;
            _startedAt = DateTimeOffset.UtcNow;
            AppendLog($"Started TradingAgents API (PID {process.Id}).");
        }

        await WaitForHealthAsync(cancellationToken);
    }

    public Task StopAsync()
    {
        var stoppedAny = false;

        lock (_sync)
        {
            if (_process is not { HasExited: false } process)
            {
                // Keep going - API may still be running but not owned by this manager.
            }
            else
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                    AppendLog("Stopped TradingAgents API process.");
                    stoppedAny = true;
                }
                catch (Exception ex)
                {
                    AppendLog($"Error while stopping process: {ex.Message}");
                }
                finally
                {
                    _process = null;
                    _startedAt = null;
                }
            }
        }

        // Also kill any process that is currently listening on API port.
        foreach (var pid in FindListeningPidsOnPort(_options.TradingAgentsApiPort))
        {
            try
            {
                if (pid == Environment.ProcessId)
                {
                    continue;
                }

                var proc = Process.GetProcessById(pid);
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
                AppendLog($"Killed external listener on port {_options.TradingAgentsApiPort} (PID {pid}).");
                stoppedAny = true;
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to kill PID {pid} on port {_options.TradingAgentsApiPort}: {ex.Message}");
            }
        }

        if (!stoppedAny)
        {
            AppendLog("No TradingAgents API process found to stop.");
        }

        return Task.CompletedTask;
    }

    public async Task WaitForHealthAsync(CancellationToken cancellationToken = default)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var baseUrl = GetBaseUrl();
        var until = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Process? procSnapshot;
            lock (_sync)
            {
                procSnapshot = _process;
            }
            if (procSnapshot is null || procSnapshot.HasExited)
            {
                var tail = string.Join(" | ", GetLogs(25));
                throw new InvalidOperationException(
                    $"TradingAgents API process exited before health check succeeded. Logs: {tail}"
                );
            }

            try
            {
                var response = await http.GetAsync($"{baseUrl}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Keep polling until timeout.
            }

            await Task.Delay(1000, cancellationToken);
        }

        var timeoutTail = string.Join(" | ", GetLogs(25));
        throw new TimeoutException($"TradingAgents API health check timed out. Logs: {timeoutTail}");
    }

    public async Task<bool> IsApiReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetAsync($"{GetBaseUrl()}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string ResolveRepoPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.TradingAgentsRepoPath))
        {
            return _options.TradingAgentsRepoPath!;
        }

        var dir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
        return path;
    }

    private (string FileName, string Arguments) BuildStartCommand()
    {
        if (_options.UseConda)
        {
            if (string.IsNullOrWhiteSpace(_options.CondaExePath))
            {
                throw new InvalidOperationException("CondaExePath is empty.");
            }

            if (!File.Exists(_options.CondaExePath))
            {
                throw new FileNotFoundException("Conda executable not found.", _options.CondaExePath);
            }

            var args = $"run --no-capture-output -n {_options.CondaEnvName} python -m {_options.TradingAgentsApiModule}";
            return (_options.CondaExePath, args);
        }

        return (_options.PythonExe, $"-m {_options.TradingAgentsApiModule}");
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_sync)
        {
            _logLines.Add($"[{DateTimeOffset.Now:HH:mm:ss}] {line}");
            if (_logLines.Count > 2000)
            {
                _logLines.RemoveRange(0, _logLines.Count - 2000);
            }
        }
    }

    private static IReadOnlyCollection<int> FindListeningPidsOnPort(int port)
    {
        var pids = new HashSet<int>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return pids;
            }

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!line.Contains($":{port}", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("ABH", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }
                var pidRaw = parts[^1];
                if (int.TryParse(pidRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                {
                    pids.Add(pid);
                }
            }
        }
        catch
        {
            // Ignore and return what we have.
        }

        return pids;
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
