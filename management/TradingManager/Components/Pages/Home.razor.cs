using TradingManager.Components.Models;

namespace TradingManager.Components.Pages;

public partial class Home : IDisposable
{
    private ManagerStatusResponse? _status;
    private List<string> _models = [];
    private string? _selectedModel;
    private string? _selectedLoadModel { get => _selectedModel; set => _selectedModel = value; }
    private string? _selectedSmallModel;
    private string? _selectedLargeModel;
    private string _ticker = "NVDA";
    private DateTime _analysisDateValue = DateTime.Today;
    private string _language = "German";
    private string _researchDepthLabel = "Shallow";
    private string _reportVerbosity = "standard";
    private string _contextLengthLabel = "128k";
    private int? _maxConcurrent;
    private readonly string[] _languages =
    [
        "English",
        "German",
    ];
    private readonly string[] _researchDepthOptions = ["Shallow", "Medium", "Deep"];
    private readonly string[] _reportVerbosityOptions = ["original", "standard", "compact"];
    private readonly string[] _contextLengthOptions = ["32k", "64k", "128k", "256k"];
    private string? _message;
    private string? _jobId;
    private string? _jobStatusJson;
    private string? _jobResultJson;
    private string? _summaryText;
    private string? _eventStreamText;
    private string _liveJobStatus = "unknown";
    private int _liveJobProgress;
    private string? _queueInfo;
    private bool _streaming;
    private CancellationTokenSource? _streamCts;
    private readonly Dictionary<string, TeamState> _teamStates = [];
    private readonly Dictionary<string, MetricValue> _keyMetrics = [];
    private readonly List<SignalHit> _signals = [];
    private bool _showLogsModal;
    private bool _showSignalDetailsModal;
    private SignalHit? _selectedSignal;
    private readonly List<ProgressTeam> _progressTeams = CreateProgressTeams();
    private bool _summaryInProgress;
    private bool _autoSummaryTriggered;
    private bool _renderSummaryMarkdown = true;
    private int _agentsCompleted;
    private int _agentsTotal = 12;
    private int _llmCalls;
    private int _toolCalls;
    private int _promptTokens;
    private int _completionTokens;
    private int _reportsCompleted;
    private int _reportsTotal = 7;
    private DateTime? _runStartedAtUtc;

    protected override async Task OnInitializedAsync()
    {
        await RefreshStatus();
        await LoadModels();
        SyncSelectedModelFromStatus();
        if (_status?.TradingAgentsApi.IsRunning == true)
        {
            await ResumeActiveRun();
        }
    }

    private HttpClient NewClient()
    {
        var client = HttpClientFactory.CreateClient();
        client.BaseAddress = new Uri(Navigation.BaseUri);
        return client;
    }

    private async Task StartApi()
    {
        try
        {
            if (_status?.TradingAgentsApi.IsRunning == true)
            {
                _message = "TradingAgents API laeuft bereits.";
                await RefreshStatus();
                await LoadModels();
                return;
            }

            using var client = NewClient();
            var response = await client.PostAsync("api/manager/start-api", null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"API Start fehlgeschlagen: {body}";
                return;
            }
            _message = $"TradingAgents API gestartet: {body}";
            await WaitForApiState(isRunning: true);
            await LoadModels();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task StopApi()
    {
        try
        {
            using var client = NewClient();
            var response = await client.PostAsync("api/manager/stop-api", null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"API Stop fehlgeschlagen: {body}";
                return;
            }
            _message = $"TradingAgents API gestoppt: {body}";
            await WaitForApiState(isRunning: false);
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task WaitForApiState(bool isRunning)
    {
        const int maxAttempts = 8;
        for (var i = 0; i < maxAttempts; i++)
        {
            await RefreshStatus();
            if ((_status?.TradingAgentsApi.IsRunning ?? false) == isRunning)
            {
                return;
            }

            await Task.Delay(350);
        }
    }

    private async Task RefreshStatus()
    {
        try
        {
            using var client = NewClient();
            _status = await client.GetFromJsonAsync<ManagerStatusResponse>("api/manager/status");
            SyncSelectedModelFromStatus();
            _message = "Status aktualisiert.";
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task LoadModels()
    {
        try
        {
            using var client = NewClient();
            var response = await client.GetAsync("api/manager/lmstudio/models");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"Modelle laden fehlgeschlagen: {body}";
                return;
            }

            var data = await response.Content.ReadFromJsonAsync<ModelListResponse>();
            _models = data?.Models?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? [];
            if (!string.IsNullOrWhiteSpace(data?.LoadedModelId) &&
                _models.Any(m => string.Equals(m, data.LoadedModelId, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedModel = data.LoadedModelId;
            }
            else if (string.IsNullOrWhiteSpace(_selectedModel) && _models.Count > 0)
            {
                _selectedModel = _models[0];
            }
            _message = _models.Count > 0
                ? $"Modelle geladen: {_models.Count}"
                : $"Antwort erhalten, aber keine Modelle gefunden: {body}";
            SyncSelectedModelFromStatus();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task LoadModelInLmStudio()
    {
        if (string.IsNullOrWhiteSpace(_selectedModel))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                "api/manager/lmstudio/load",
                new
                {
                    modelId = _selectedModel,
                    contextLength = MapContextLength(_contextLengthLabel),
                    maxConcurrent = _maxConcurrent,
                });
            response.EnsureSuccessStatusCode();
            _message = $"Modell '{_selectedModel}' in LM Studio geladen und gesetzt.";
            await RefreshStatus();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task SetModelOnly()
    {
        if (string.IsNullOrWhiteSpace(_selectedModel))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                "api/manager/tradingagents/select-model",
                new { modelId = _selectedModel });
            response.EnsureSuccessStatusCode();
            _message = $"Modell '{_selectedModel}' in TradingManager gesetzt.";
            await RefreshStatus();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task SetModelsOnly()
    {
        if (string.IsNullOrWhiteSpace(_selectedSmallModel) && string.IsNullOrWhiteSpace(_selectedLargeModel) && string.IsNullOrWhiteSpace(_selectedModel))
        {
            return;
        }

        _selectedSmallModel ??= _selectedModel;
        _selectedLargeModel ??= _selectedModel;
        _selectedModel = _selectedLargeModel ?? _selectedSmallModel;

        try
        {
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                "api/manager/tradingagents/select-models",
                new
                {
                    smallModelId = _selectedSmallModel,
                    largeModelId = _selectedLargeModel,
                });
            if (!response.IsSuccessStatusCode)
            {
                await SetModelOnly();
                return;
            }

            await RefreshStatus();
        }
        catch
        {
            await SetModelOnly();
        }
    }

    private async Task StartAnalysis()
    {
        try
        {
            await EnsureModelSelectionForRun();
            await EnsureSelectedModelIsActive();

            if (!HasAnyModelSelection)
            {
                _message = "Kein Modell ausgewaehlt. Bitte zuerst ein LM Studio Modell laden.";
                return;
            }

            ResetProgressTeams();
            _keyMetrics.Clear();
            _signals.Clear();
            _autoSummaryTriggered = false;
            _summaryInProgress = false;
            var shouldQueue = HasActiveRun;
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                "api/manager/tradingagents/analyze",
                new
                {
                    ticker = _ticker,
                    analysisDate = _analysisDateValue.ToString("yyyy-MM-dd"),
                    provider = "lmstudio",
                    researchDepth = MapResearchDepth(_researchDepthLabel),
                    language = _language,
                    smallModelId = _selectedSmallModel ?? _selectedModel,
                    largeModelId = _selectedLargeModel ?? _selectedModel,
                    reportVerbosity = _reportVerbosity,
                });

            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"Analyse konnte nicht gestartet werden: {body}";
                return;
            }

            _message = shouldQueue ? $"Run in Queue gestellt: {body}" : $"Analyse gestartet: {body}";
            try
            {
                var data = await response.Content.ReadFromJsonAsync<AnalyzeStartResponse>();
                _jobId = data?.JobId;
                _queueInfo = shouldQueue ? "queued" : null;
                _jobStatusJson = null;
                _jobResultJson = null;
                _eventStreamText = null;
                _summaryText = null;
                _teamStates.Clear();
                _keyMetrics.Clear();
                _signals.Clear();
                _selectedSignal = null;
                _showSignalDetailsModal = false;

                try
                {
                    using var startDoc = System.Text.Json.JsonDocument.Parse(body);
                    if (startDoc.RootElement.TryGetProperty("status", out var startStatus))
                    {
                        _liveJobStatus = startStatus.GetString() ?? _liveJobStatus;
                    }
                    if (startDoc.RootElement.TryGetProperty("queue_position", out var qp))
                    {
                        _queueInfo = $"position {qp}";
                    }
                }
                catch
                {
                    // Keep defaults when response shape differs.
                }

                if (!string.IsNullOrWhiteSpace(_jobId))
                {
                    StartEventStreamForJob(_jobId);
                    await PersistCurrentRunSnapshot();
                }
            }
            catch
            {
                // Keep raw message if parsing fails.
            }
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task ToggleEventStream()
    {
        if (_streaming)
        {
            _streamCts?.Cancel();
            _streaming = false;
            _message = "Live-Stream gestoppt.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        _streaming = true;
        _streamCts = new CancellationTokenSource();
        _ = Task.Run(() => PollEventsLoop(_jobId!, _streamCts.Token));
        _message = "Live-Stream gestartet.";
    }

    private async Task UnloadModelInLmStudio()
    {
        try
        {
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                "api/manager/lmstudio/unload",
                new { modelId = _selectedModel });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"Modell-Auswurf fehlgeschlagen: {body}";
                return;
            }

            _message = "Modell in LM Studio ausgeworfen.";
            _selectedModel = null;
            _selectedSmallModel = null;
            _selectedLargeModel = null;
            await RefreshStatus();
            await LoadModels();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private void StartEventStreamForJob(string jobId)
    {
        _streamCts?.Cancel();
        _streaming = true;
        _streamCts = new CancellationTokenSource();
        _ = Task.Run(() => PollEventsLoop(jobId, _streamCts.Token));
        _message = $"Analyse gestartet und Live-Stream aktiviert: {jobId}";
    }

    private async Task PollEventsLoop(string jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = NewClient();
                var response = await client.GetAsync($"api/manager/tradingagents/jobs/{jobId}/events", ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    await InvokeAsync(() =>
                    {
                        _eventStreamText = $"Event-Fehler: {body}";
                        StateHasChanged();
                    });
                    break;
                }

                var parsed = System.Text.Json.JsonDocument.Parse(body);
                var lines = new List<string>();
                var stateUpdates = new List<TeamState>();
                if (parsed.RootElement.TryGetProperty("events", out var events) &&
                    events.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var ev in events.EnumerateArray().TakeLast(120))
                    {
                        var ts = ev.TryGetProperty("ts", out var tsProp) ? tsProp.GetString() : "";
                        var type = ev.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "event";
                        var msg = ev.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "";
                        var section = ev.TryGetProperty("section", out var secProp) ? secProp.GetString() : "";
                        var team = ev.TryGetProperty("team", out var teamProp) ? teamProp.GetString() : "System";
                        var agent = ev.TryGetProperty("agent", out var agentProp) ? agentProp.GetString() : "System";
                        var stage = ev.TryGetProperty("stage", out var stageProp) ? stageProp.GetString() : "";
                        var phase = ev.TryGetProperty("phase", out var phaseProp) ? phaseProp.GetString() : "";
                        var progress = ev.TryGetProperty("progress", out var progressProp) && progressProp.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? progressProp.GetInt32()
                            : (int?)null;
                        var hasStatusData = !string.IsNullOrWhiteSpace(stage)
                            || !string.IsNullOrWhiteSpace(phase)
                            || progress.HasValue;
                        var eventType = type?.ToLowerInvariant() ?? "event";
                        var isStatusEvent = eventType is "section" or "stage" or "heartbeat" or "chunk";
                        var isDisplayTeam = team is "System" or "Analyst Team" or "Research Team" or "Trading Team" or "Risk & Portfolio";

                        // Update team cards only from structured status events.
                        if (!string.IsNullOrWhiteSpace(team) && isDisplayTeam && isStatusEvent && hasStatusData)
                        {
                            _teamStates.TryGetValue(team, out var existingState);
                            stateUpdates.Add(new TeamState
                            {
                                Team = team,
                                Agent = !string.IsNullOrWhiteSpace(agent) ? agent : existingState?.Agent ?? "System",
                                Stage = !string.IsNullOrWhiteSpace(stage) ? stage : existingState?.Stage ?? "",
                                Phase = !string.IsNullOrWhiteSpace(phase) ? phase : existingState?.Phase ?? "",
                                Progress = progress ?? existingState?.Progress ?? 0,
                            });
                        }

                        var suffix = string.IsNullOrWhiteSpace(section) ? "" : $" [{section}]";
                        lines.Add($"{ts} | {team}/{agent} | {type}{suffix} | {msg}");
                        UpdateProgressWindow(eventType, section ?? string.Empty, stage ?? string.Empty);
                        ExtractMetricsFromText(
                            msg ?? string.Empty,
                            ts ?? string.Empty,
                            eventType,
                            section ?? string.Empty,
                            team ?? string.Empty);
                        ExtractSignals(
                            msg ?? string.Empty,
                            team ?? "System",
                            section ?? string.Empty,
                            ts ?? string.Empty);
                    }
                }

                await InvokeAsync(() =>
                {
                    foreach (var update in stateUpdates)
                    {
                        if (_teamStates.TryGetValue(update.Team, out var existing))
                        {
                            // Prevent regressions due to sparse events.
                            update.Progress = Math.Max(existing.Progress, update.Progress);
                        }
                        _teamStates[update.Team] = update;
                    }
                    _eventStreamText = string.Join(Environment.NewLine, lines);
                    StateHasChanged();
                });

                // Keep progress/status in sync even when no new message/section events arrive.
                var statusResponse = await client.GetAsync($"api/manager/tradingagents/jobs/{jobId}/status", ct);
                var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);
                if (statusResponse.IsSuccessStatusCode)
                {
                    try
                    {
                        var statusDoc = System.Text.Json.JsonDocument.Parse(statusBody);
                        var root = statusDoc.RootElement;
                        var liveStatus = root.TryGetProperty("status", out var statusProp)
                            ? statusProp.GetString() ?? "unknown"
                            : "unknown";
                        var liveProgress = root.TryGetProperty("progress", out var progressProp) &&
                                           progressProp.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? progressProp.GetInt32()
                            : _liveJobProgress;

                        await InvokeAsync(() =>
                        {
                            _jobStatusJson = statusBody;
                            _liveJobStatus = liveStatus;
                            _liveJobProgress = liveProgress;
                            _queueInfo = ExtractQueueInfo(root);
                            UpdateProgressFromGlobalStatus(liveStatus);
                            if (_teamStates.TryGetValue("System", out var systemState))
                            {
                                systemState.Progress = Math.Max(systemState.Progress, _liveJobProgress);
                                systemState.Stage = liveStatus;
                            }
                            StateHasChanged();
                        });
                        await PersistCurrentRunSnapshot();

                        if ((liveStatus == "succeeded" || liveStatus == "completed") &&
                            !_autoSummaryTriggered &&
                            !_summaryInProgress)
                        {
                            _autoSummaryTriggered = true;
                            _ = SummarizeResult(silent: true);
                        }
                    }
                    catch
                    {
                        // Ignore parse issue and keep stream running.
                    }
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await InvokeAsync(() =>
                {
                    _eventStreamText = $"Stream-Exception: {ex.Message}";
                    StateHasChanged();
                });
                break;
            }

            try
            {
                await Task.Delay(2000, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        await InvokeAsync(() =>
        {
            _streaming = false;
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
    }

    private async Task LoadJobStatus()
    {
        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.GetAsync($"api/manager/tradingagents/jobs/{_jobId}/status");
            _jobStatusJson = await response.Content.ReadAsStringAsync();
            _message = response.IsSuccessStatusCode ? "Job-Status geladen." : $"Job-Status Fehler: {_jobStatusJson}";
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task LoadJobResult()
    {
        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.GetAsync($"api/manager/tradingagents/jobs/{_jobId}/result");
            _jobResultJson = await response.Content.ReadAsStringAsync();
            _message = response.IsSuccessStatusCode ? "Job-Ergebnis geladen." : $"Job-Ergebnis Fehler: {_jobResultJson}";
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task SummarizeResult(bool silent = false)
    {
        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        try
        {
            _summaryInProgress = true;
            using var client = NewClient();
            var response = await client.PostAsJsonAsync(
                $"api/manager/tradingagents/jobs/{_jobId}/summarize",
                new { language = _language });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (body.Contains("kein model", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("no model", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsureModelSelectionForRun();
                    await EnsureSelectedModelIsActive();
                    response = await client.PostAsJsonAsync(
                        $"api/manager/tradingagents/jobs/{_jobId}/summarize",
                        new { language = _language });
                    body = await response.Content.ReadAsStringAsync();
                }

                if (!response.IsSuccessStatusCode)
                {
                    _message = $"Summarize fehlgeschlagen: {body}";
                    return;
                }
            }

            try
            {
                var data = System.Text.Json.JsonDocument.Parse(body);
                if (data.RootElement.TryGetProperty("summary", out var summaryProp))
                {
                    _summaryText = summaryProp.GetString() ?? body;
                }
                else
                {
                    _summaryText = body;
                }
            }
            catch
            {
                _summaryText = body;
            }

            if (!silent)
            {
                _message = "Ergebnis zusammengefasst.";
            }
            await PersistCurrentRunSnapshot();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
        finally
        {
            _summaryInProgress = false;
        }
    }

    private async Task CancelAnalysis()
    {
        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.PostAsync($"api/manager/tradingagents/jobs/{_jobId}/cancel", null);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _message = $"Analyse-Stop fehlgeschlagen: {body}";
                return;
            }

            _message = $"Analyse-Stop angefordert: {body}";
            await LoadJobStatus();
        }
        catch (Exception ex)
        {
            _message = ex.Message;
        }
    }

    private async Task ResumeActiveRun()
    {
        if (_status?.TradingAgentsApi.IsRunning != true)
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var response = await client.GetAsync("api/manager/tradingagents/jobs?status=running&limit=1");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 409 && body.Contains("outdated", StringComparison.OrdinalIgnoreCase))
                {
                    _message = "Resume nicht verfuegbar: Bitte TradingAgents API einmal stoppen und neu starten (neue Endpoints laden).";
                    return;
                }
                _message = $"Resume-Abfrage fehlgeschlagen: {body}";
                return;
            }

            var data = System.Text.Json.JsonDocument.Parse(body);
            if (!data.RootElement.TryGetProperty("jobs", out var jobs) ||
                jobs.ValueKind != System.Text.Json.JsonValueKind.Array ||
                jobs.GetArrayLength() == 0)
            {
                // fallback: queued/cancel_requested
                response = await client.GetAsync("api/manager/tradingagents/jobs?limit=10");
                body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }
                data = System.Text.Json.JsonDocument.Parse(body);
                if (!data.RootElement.TryGetProperty("jobs", out jobs) ||
                    jobs.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return;
                }

                var candidate = jobs.EnumerateArray()
                    .FirstOrDefault(j =>
                        j.TryGetProperty("status", out var st) &&
                        (st.GetString() == "running" || st.GetString() == "queued" || st.GetString() == "cancel_requested"));
                if (candidate.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                {
                    return;
                }
                if (candidate.TryGetProperty("job_id", out var idProp))
                {
                    _jobId = idProp.GetString();
                }
            }
            else
            {
                var first = jobs[0];
                if (first.TryGetProperty("job_id", out var idProp))
                {
                    _jobId = idProp.GetString();
                }
            }

            if (!string.IsNullOrWhiteSpace(_jobId))
            {
                _message = $"Aktiver Run wiederhergestellt: {_jobId}";
                await LoadJobStatus();
                await PersistCurrentRunSnapshot();
            }
        }
        catch (HttpRequestException)
        {
            // API not reachable yet; non-fatal on page start.
        }
        catch
        {
            // non-fatal on page start
        }
    }

    private async Task EnsureModelSelectionForRun()
    {
        if (!string.IsNullOrWhiteSpace(_selectedModel))
        {
            return;
        }

        await LoadModels();
    }

    private async Task EnsureSelectedModelIsActive()
    {
        if (!HasAnyModelSelection)
        {
            return;
        }

        if (!string.Equals(_status?.SelectedModel, _selectedLargeModel ?? _selectedSmallModel ?? _selectedModel, StringComparison.OrdinalIgnoreCase))
        {
            await SetModelsOnly();
        }
    }

    private void SyncSelectedModelFromStatus()
    {
        var statusModel = _status?.SelectedModel;
        if (!string.IsNullOrWhiteSpace(statusModel))
        {
            _selectedModel = statusModel;
            _selectedLoadModel ??= statusModel;
            _selectedSmallModel ??= _status?.SmallModel ?? statusModel;
            _selectedLargeModel ??= _status?.LargeModel ?? statusModel;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedModel) && _models.Count > 0)
        {
            _selectedModel = _models[0];
            _selectedLoadModel = _selectedModel;
            _selectedSmallModel ??= _selectedModel;
            _selectedLargeModel ??= _selectedModel;
        }
    }

    private bool HasActiveRun => _liveJobStatus is "running" or "queued" or "cancel_requested";
    private bool HasAnyModelSelection =>
        !string.IsNullOrWhiteSpace(_selectedSmallModel) ||
        !string.IsNullOrWhiteSpace(_selectedLargeModel) ||
        !string.IsNullOrWhiteSpace(_selectedModel);

    private int GetSignalCount(string signal) =>
        _signals.Count(s => string.Equals(s.Signal, signal, StringComparison.OrdinalIgnoreCase));

    private (string Label, int Percent, string CssClass) ComputeSignalRecommendation()
    {
        var buy = GetSignalCount("BUY");
        var hold = GetSignalCount("HOLD");
        var sell = GetSignalCount("SELL");
        var total = buy + hold + sell;
        if (total == 0)
        {
            return ("HOLD", 0, GetSignalCss("HOLD"));
        }

        // Conservative vote: only recommend BUY/SELL on clear dominance.
        // Otherwise fall back to HOLD.
        var diffBuySell = buy - sell;
        var directionalShare = Math.Abs(diffBuySell) * 100.0 / total;
        var hasClearDirectionalMajority = directionalShare >= 40.0; // practical threshold for mixed signals

        if (!hasClearDirectionalMajority)
        {
            var holdPct = (int)Math.Round(hold * 100.0 / total, MidpointRounding.AwayFromZero);
            return ("HOLD", holdPct, GetSignalCss("HOLD"));
        }

        var label = diffBuySell > 0 ? "BUY" : "SELL";
        var pct = (int)Math.Round(Math.Max(buy, sell) * 100.0 / total, MidpointRounding.AwayFromZero);
        return (label, pct, GetSignalCss(label));
    }

    private void OpenSignalDetails(SignalHit signal)
    {
        _selectedSignal = signal;
        _showSignalDetailsModal = true;
    }

    private async Task PersistCurrentRunSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_jobId))
        {
            return;
        }

        try
        {
            using var client = NewClient();
            var snapshot = new RunSnapshotDto
            {
                JobId = _jobId!,
                Ticker = _ticker,
                AnalysisDate = _analysisDateValue.ToString("yyyy-MM-dd"),
                Provider = "lmstudio",
                ModelId = _selectedModel,
                ResearchDepth = _researchDepthLabel,
                Language = _language,
                ReportVerbosity = _reportVerbosity,
                ContextLength = MapContextLength(_contextLengthLabel),
                MaxConcurrent = _maxConcurrent,
                Status = _liveJobStatus,
                Progress = _liveJobProgress,
                QueueInfo = _queueInfo,
                Summary = _summaryText,
                RawStatusJson = _jobStatusJson,
                RawResultJson = _jobResultJson,
                Metrics = _keyMetrics.Select(kv => new PersistedMetricDto
                {
                    Key = kv.Key,
                    Value = kv.Value.Value,
                    UpdatedAt = kv.Value.UpdatedAt,
                    Confidence = kv.Value.Confidence,
                }).ToList(),
                Signals = _signals.Select(s => new PersistedSignalDto
                {
                    Signal = s.Signal,
                    Source = s.Source,
                    Context = s.Context,
                    UpdatedAt = s.UpdatedAt.ToString("O"),
                }).ToList(),
            };

            await client.PostAsJsonAsync("api/manager/runs/upsert", snapshot);
        }
        catch
        {
            // Persistence is best-effort and should not break the run UI.
        }
    }


    private static List<ProgressTeam> CreateProgressTeams() =>
    [
        new ProgressTeam("Analyst Team",
        [
            new AgentStatusItem("Market Analyst"),
            new AgentStatusItem("Social Analyst"),
            new AgentStatusItem("News Analyst"),
            new AgentStatusItem("Fundamentals Analyst"),
        ]),
        new ProgressTeam("Research Team",
        [
            new AgentStatusItem("Bull Researcher"),
            new AgentStatusItem("Bear Researcher"),
            new AgentStatusItem("Research Manager"),
        ]),
        new ProgressTeam("Trading Team",
        [
            new AgentStatusItem("Trader"),
        ]),
        new ProgressTeam("Risk Management",
        [
            new AgentStatusItem("Aggressive Analyst"),
            new AgentStatusItem("Neutral Analyst"),
            new AgentStatusItem("Conservative Analyst"),
        ]),
        new ProgressTeam("Portfolio Management",
        [
            new AgentStatusItem("Portfolio Manager"),
        ]),
    ];

    private void ResetProgressTeams()
    {
        foreach (var team in _progressTeams)
        {
            foreach (var agent in team.Agents)
            {
                agent.Status = "pending";
            }
        }
    }

    private void UpdateProgressWindow(string eventType, string section, string stage)
    {
        if (eventType == "section")
        {
            switch (section)
            {
                case "market_report":
                    SetAgentStatus("Market Analyst", "completed");
                    SetAgentStatus("Social Analyst", "in_progress");
                    break;
                case "sentiment_report":
                    SetAgentStatus("Social Analyst", "completed");
                    SetAgentStatus("News Analyst", "in_progress");
                    break;
                case "news_report":
                    SetAgentStatus("News Analyst", "completed");
                    SetAgentStatus("Fundamentals Analyst", "in_progress");
                    break;
                case "fundamentals_report":
                    SetAgentStatus("Fundamentals Analyst", "completed");
                    SetAgentStatus("Bull Researcher", "in_progress");
                    break;
                case "investment_plan":
                    SetAgentStatus("Bull Researcher", "completed");
                    SetAgentStatus("Bear Researcher", "completed");
                    SetAgentStatus("Research Manager", "completed");
                    SetAgentStatus("Trader", "in_progress");
                    break;
                case "trader_investment_plan":
                    SetAgentStatus("Trader", "completed");
                    SetAgentStatus("Aggressive Analyst", "in_progress");
                    break;
                case "final_trade_decision":
                    SetAgentStatus("Aggressive Analyst", "completed");
                    SetAgentStatus("Neutral Analyst", "completed");
                    SetAgentStatus("Conservative Analyst", "completed");
                    SetAgentStatus("Portfolio Manager", "completed");
                    break;
            }
        }

        if (stage == "starting")
        {
            SetAgentStatus("Market Analyst", "in_progress");
        }
    }

    private void UpdateProgressFromGlobalStatus(string status)
    {
        if (status is "succeeded" or "completed")
        {
            foreach (var team in _progressTeams)
            {
                foreach (var agent in team.Agents)
                {
                    agent.Status = "completed";
                }
            }
        }
        else if (status is "failed" or "canceled")
        {
            // Keep current statuses; this indicates the run ended early.
        }
    }

    private void SetAgentStatus(string agentName, string status)
    {
        foreach (var team in _progressTeams)
        {
            var agent = team.Agents.FirstOrDefault(a => a.AgentName == agentName);
            if (agent is not null)
            {
                agent.Status = status;
                return;
            }
        }
    }

    private static string GetStatusCss(string status) => status switch
    {
        "completed" => "status-completed",
        "in_progress" => "status-in-progress",
        _ => "status-pending",
    };

    private static string GetSignalCss(string signal) => signal switch
    {
        "BUY" => "signal-buy",
        "SELL" => "signal-sell",
        _ => "signal-hold",
    };


}
