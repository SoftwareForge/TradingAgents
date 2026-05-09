using TradingManager.Components;
using TradingManager.Options;
using TradingManager.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<ManagerOptions>(builder.Configuration.GetSection("Manager"));
builder.Services.AddSingleton<TradingAgentsProcessService>();
builder.Services.AddSingleton<ModelSelectionState>();
builder.Services.AddHttpClient<LmStudioService>();
builder.Services.AddHttpClient<TradingAgentsApiClient>();
builder.Services.AddSingleton<RunPersistenceService>();
builder.Services.AddSingleton<MarkdownRenderService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var manager = app.MapGroup("/api/manager");

manager.MapGet("/status", async (
    TradingAgentsProcessService process,
    ModelSelectionState modelState) =>
{
    var status = await process.GetStatusAsync();
    var models = await modelState.GetModelsAsync();
    var selectedModel = models.LargeModelId ?? models.SmallModelId;
    return Results.Ok(new
    {
        tradingAgentsApi = new
        {
            isRunning = status.IsRunning,
            pid = status.Pid,
            startedAt = status.StartedAt,
            baseUrl = process.GetBaseUrl(),
        },
        selectedModel,
        smallModel = models.SmallModelId,
        largeModel = models.LargeModelId,
    });
});

manager.MapPost("/start-api", async (TradingAgentsProcessService process, CancellationToken ct) =>
{
    try
    {
        await process.StartAsync(ct);
        return Results.Ok(new { started = true, baseUrl = process.GetBaseUrl() });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to start TradingAgents API: {ex.Message}");
    }
});

manager.MapPost("/stop-api", async (TradingAgentsProcessService process) =>
{
    await process.StopAsync();
    return Results.Ok(new { stopped = true });
});

manager.MapGet("/logs", (TradingAgentsProcessService process, int tail = 200) =>
{
    return Results.Ok(new { lines = process.GetLogs(tail) });
});

manager.MapGet("/lmstudio/models", async (LmStudioService lmStudio, CancellationToken ct) =>
{
    var (models, loadedModelId) = await lmStudio.GetModelsInfoAsync(ct);
    return Results.Ok(new
    {
        models = models.Select(m => m.Id),
        loadedModelId,
    });
});

manager.MapPost("/lmstudio/load", async (
    LmStudioService lmStudio,
    ModelSelectionState modelState,
    LoadModelRequest req,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ModelId))
    {
        return Results.BadRequest(new { error = "modelId is required" });
    }

    var loaded = await lmStudio.LoadModelAsync(
        req.ModelId,
        req.ContextLength,
        req.MaxConcurrent,
        ct);
    if (!loaded)
    {
        return Results.Problem("Failed to load model in LM Studio.");
    }

    if (!string.IsNullOrWhiteSpace(req.TargetRole))
    {
        await modelState.SetModelForRoleAsync(req.TargetRole, req.ModelId);
    }
    else
    {
        await modelState.SetModelAsync(req.ModelId);
    }

    var models = await modelState.GetModelsAsync();
    return Results.Ok(new
    {
        loaded = true,
        selectedModel = models.LargeModelId ?? models.SmallModelId,
        smallModel = models.SmallModelId,
        largeModel = models.LargeModelId,
    });
});

manager.MapPost("/lmstudio/unload", async (
    LmStudioService lmStudio,
    ModelSelectionState modelState,
    UnloadModelRequest req,
    CancellationToken ct) =>
{
    var modelId = string.IsNullOrWhiteSpace(req.ModelId)
        ? await modelState.GetModelAsync()
        : req.ModelId;

    var unloaded = await lmStudio.UnloadModelAsync(modelId, ct);
    if (!unloaded)
    {
        return Results.Problem("Failed to unload model in LM Studio.");
    }

    if (!string.IsNullOrWhiteSpace(modelId))
    {
        var models = await modelState.GetModelsAsync();
        var nextSmall = string.Equals(models.SmallModelId, modelId, StringComparison.OrdinalIgnoreCase) ? null : models.SmallModelId;
        var nextLarge = string.Equals(models.LargeModelId, modelId, StringComparison.OrdinalIgnoreCase) ? null : models.LargeModelId;
        await modelState.SetModelsAsync(nextSmall, nextLarge);
    }
    else
    {
        await modelState.SetModelsAsync(null, null);
    }

    return Results.Ok(new { unloaded = true });
});

manager.MapPost("/tradingagents/select-model", async (
    ModelSelectionState modelState,
    SelectModelRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.ModelId))
    {
        return Results.BadRequest(new { error = "modelId is required" });
    }

    if (!string.IsNullOrWhiteSpace(req.Role))
    {
        await modelState.SetModelForRoleAsync(req.Role, req.ModelId);
    }
    else
    {
        await modelState.SetModelAsync(req.ModelId);
    }

    var models = await modelState.GetModelsAsync();
    return Results.Ok(new
    {
        selectedModel = models.LargeModelId ?? models.SmallModelId,
        smallModel = models.SmallModelId,
        largeModel = models.LargeModelId,
    });
});

manager.MapPost("/tradingagents/select-models", async (
    ModelSelectionState modelState,
    SelectModelsRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.SmallModelId) && string.IsNullOrWhiteSpace(req.LargeModelId))
    {
        return Results.BadRequest(new { error = "At least one model id is required." });
    }

    var current = await modelState.GetModelsAsync();
    var resolvedSmall = string.IsNullOrWhiteSpace(req.SmallModelId) ? current.SmallModelId : req.SmallModelId;
    var resolvedLarge = string.IsNullOrWhiteSpace(req.LargeModelId) ? current.LargeModelId : req.LargeModelId;

    if (string.IsNullOrWhiteSpace(resolvedSmall) && !string.IsNullOrWhiteSpace(resolvedLarge))
    {
        resolvedSmall = resolvedLarge;
    }
    if (string.IsNullOrWhiteSpace(resolvedLarge) && !string.IsNullOrWhiteSpace(resolvedSmall))
    {
        resolvedLarge = resolvedSmall;
    }

    await modelState.SetModelsAsync(resolvedSmall, resolvedLarge);
    return Results.Ok(new
    {
        selectedModel = resolvedLarge ?? resolvedSmall,
        smallModel = resolvedSmall,
        largeModel = resolvedLarge,
    });
});

manager.MapPost("/tradingagents/analyze", async (
    TradingAgentsApiClient apiClient,
    AnalyzeRequest req,
    CancellationToken ct) =>
{
    var result = await apiClient.StartAnalysisWithSelectedModelAsync(
        req.Ticker,
        req.AnalysisDate,
        req.Provider,
        req.ResearchDepth,
        req.Language,
        req.SmallModelId,
        req.LargeModelId,
        req.ReportVerbosity,
        ct);

    if (!result.Ok)
    {
        return Results.Problem(result.Message);
    }

    return Results.Ok(new { started = true, response = result.Message, jobId = result.JobId });
});

manager.MapGet("/tradingagents/jobs/{jobId}/status", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    string jobId,
    CancellationToken ct) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var response = await http.GetAsync($"{baseUrl}/v1/analyses/{jobId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

manager.MapGet("/tradingagents/jobs/{jobId}/result", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    string jobId,
    CancellationToken ct) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var response = await http.GetAsync($"{baseUrl}/v1/analyses/{jobId}/result", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

manager.MapGet("/tradingagents/jobs/{jobId}/events", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    string jobId,
    CancellationToken ct) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var response = await http.GetAsync($"{baseUrl}/v1/analyses/{jobId}/events", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

manager.MapPost("/tradingagents/jobs/{jobId}/cancel", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    string jobId,
    CancellationToken ct) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var response = await http.PostAsync($"{baseUrl}/v1/analyses/{jobId}/cancel", null, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

manager.MapPost("/tradingagents/jobs/{jobId}/summarize", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    ModelSelectionState modelState,
    LmStudioService lmStudioService,
    string jobId,
    SummarizeRequest req,
    CancellationToken ct) =>
{
    var modelId = await modelState.GetModelAsync();
    if (string.IsNullOrWhiteSpace(modelId))
    {
        return Results.BadRequest(new { error = "No model selected for summarization." });
    }

    string body;
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var response = await http.GetAsync($"{baseUrl}/v1/analyses/{jobId}/result", ct);
        body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
        }
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    string finalDecision = body;
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("final_trade_decision", out var finalProp) &&
            finalProp.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            finalDecision = finalProp.GetString() ?? body;
        }
    }
    catch
    {
        // fallback to raw body
    }

    try
    {
        var summary = await lmStudioService.SummarizeTextAsync(
            modelId,
            finalDecision,
            string.IsNullOrWhiteSpace(req.Language) ? "English" : req.Language,
            ct);
        return Results.Ok(new { model = modelId, summary });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Summarization failed: {ex.Message}");
    }
});

manager.MapGet("/tradingagents/jobs", async (
    TradingAgentsProcessService process,
    IHttpClientFactory httpClientFactory,
    string? status,
    int limit,
    CancellationToken ct) =>
{
    try
    {
        var http = httpClientFactory.CreateClient();
        var baseUrl = process.GetBaseUrl();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }
        if (limit > 0)
        {
            query.Add($"limit={limit}");
        }
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : string.Empty;
        var response = await http.GetAsync($"{baseUrl}/v1/analyses{qs}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
        {
            return Results.Problem(
                "TradingAgents API instance is outdated (missing GET /v1/analyses). Please restart the TradingAgents API from the Manager UI.",
                statusCode: StatusCodes.Status409Conflict);
        }
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            $"TradingAgents API nicht erreichbar ({process.GetBaseUrl()}): {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

manager.MapPost("/runs/upsert", async (
    RunPersistenceService persistence,
    RunSnapshot snapshot,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(snapshot.JobId))
    {
        return Results.BadRequest(new { error = "jobId is required" });
    }

    await persistence.UpsertAsync(snapshot, ct);
    return Results.Ok(new { saved = true, jobId = snapshot.JobId });
});

manager.MapGet("/runs", async (
    RunPersistenceService persistence,
    int limit,
    CancellationToken ct) =>
{
    var runs = await persistence.ListAsync(limit <= 0 ? 100 : limit, ct);
    return Results.Ok(new { runs });
});

manager.MapGet("/runs/{jobId}", async (
    RunPersistenceService persistence,
    string jobId,
    CancellationToken ct) =>
{
    var run = await persistence.GetAsync(jobId, ct);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.Run();

sealed record LoadModelRequest(string ModelId, int? ContextLength = null, int? MaxConcurrent = null, string? TargetRole = null);
sealed record UnloadModelRequest(string? ModelId = null);
sealed record SelectModelRequest(string ModelId, string? Role = null);
sealed record SelectModelsRequest(string? SmallModelId = null, string? LargeModelId = null);
sealed record AnalyzeRequest(
    string Ticker,
    string AnalysisDate,
    string Provider = "lmstudio",
    int ResearchDepth = 1,
    string Language = "German",
    string? SmallModelId = null,
    string? LargeModelId = null,
    string ReportVerbosity = "standard");
sealed record SummarizeRequest(string Language = "English");
