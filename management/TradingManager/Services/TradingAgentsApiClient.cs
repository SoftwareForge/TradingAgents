using System.Net.Http.Json;

namespace TradingManager.Services;

public sealed class TradingAgentsApiClient
{
    private readonly HttpClient _http;
    private readonly TradingAgentsProcessService _processService;
    private readonly ModelSelectionState _modelState;

    public TradingAgentsApiClient(
        HttpClient http,
        TradingAgentsProcessService processService,
        ModelSelectionState modelState)
    {
        _http = http;
        _processService = processService;
        _modelState = modelState;
    }

    public async Task<(bool Ok, string Message, string? JobId)> StartAnalysisWithSelectedModelAsync(
        string ticker,
        string analysisDate,
        string provider = "lmstudio",
        int researchDepth = 1,
        string language = "German",
        string? smallModelOverride = null,
        string? largeModelOverride = null,
        string reportVerbosity = "standard",
        CancellationToken cancellationToken = default)
    {
        var currentModels = await _modelState.GetModelsAsync();
        var resolvedSmall = string.IsNullOrWhiteSpace(smallModelOverride) ? currentModels.SmallModelId : smallModelOverride;
        var resolvedLarge = string.IsNullOrWhiteSpace(largeModelOverride) ? currentModels.LargeModelId : largeModelOverride;

        if (string.IsNullOrWhiteSpace(resolvedSmall) && !string.IsNullOrWhiteSpace(resolvedLarge))
        {
            resolvedSmall = resolvedLarge;
        }
        if (string.IsNullOrWhiteSpace(resolvedLarge) && !string.IsNullOrWhiteSpace(resolvedSmall))
        {
            resolvedLarge = resolvedSmall;
        }

        if (string.IsNullOrWhiteSpace(resolvedSmall) || string.IsNullOrWhiteSpace(resolvedLarge))
        {
            return (false, "No LM Studio model selected for small/large roles.", null);
        }

        var payload = new
        {
            ticker,
            analysis_date = analysisDate,
            provider,
            deep_model = resolvedLarge,
            quick_model = resolvedSmall,
            research_depth = researchDepth,
            language,
            report_verbosity = reportVerbosity,
        };

        var baseUrl = _processService.GetBaseUrl();
        using var response = await _http.PostAsJsonAsync($"{baseUrl}/v1/analyses", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (false, $"TradingAgents API returned {(int)response.StatusCode}: {body}", null);
        }

        var data = await response.Content.ReadAsStringAsync(cancellationToken);
        string? jobId = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("job_id", out var jobIdProp) && jobIdProp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                jobId = jobIdProp.GetString();
            }
        }
        catch
        {
            // Keep raw payload if parsing fails.
        }

        return (true, data, jobId);
    }
}
