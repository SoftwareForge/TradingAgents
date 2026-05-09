using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingManager.Options;

namespace TradingManager.Services;

public sealed class LmStudioService
{
    private readonly HttpClient _httpClient;
    private readonly ManagerOptions _options;

    public LmStudioService(HttpClient httpClient, IOptions<ManagerOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public sealed record LmStudioModelInfo(string Id, bool IsLoaded);

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetModelsInfoAsync(cancellationToken);
        return result.Models.Select(m => m.Id).ToList();
    }

    public async Task<(IReadOnlyList<LmStudioModelInfo> Models, string? LoadedModelId)> GetModelsInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.LmStudioBaseUrl.TrimEnd('/');
        var candidates = new[]
        {
            // Prefer v0 first because it includes load state ("state": "loaded").
            $"{baseUrl}/api/v0/models",
            $"{baseUrl}/v1/models",
            $"{baseUrl}/api/v1/models",
        };

        foreach (var url in candidates)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var models = ParseModels(json);
                if (models.Count > 0)
                {
                    var loaded = models.FirstOrDefault(m => m.IsLoaded)?.Id;
                    return (models, loaded);
                }
            }
            catch
            {
                // Try next endpoint candidate.
            }
        }

        return ([], null);
    }

    public async Task<bool> LoadModelAsync(
        string modelId,
        int? contextLength = null,
        int? maxConcurrent = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.LmStudioBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/v1/models/load";
        var payloads = new object[]
        {
            BuildLoadPayload(modelId, contextLength, maxConcurrent, includeIdentifier: false),
            BuildLoadPayload(modelId, contextLength, maxConcurrent, includeIdentifier: true),
            new { model = modelId },
            new { model = modelId, identifier = modelId },
        };

        foreach (var payload in payloads)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Try next payload shape.
            }
        }

        return false;
    }

    public async Task<bool> UnloadModelAsync(
        string? modelId = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.LmStudioBaseUrl.TrimEnd('/');
        var urls = new[]
        {
            $"{baseUrl}/api/v1/models/unload",
            $"{baseUrl}/api/v0/models/unload",
        };

        var payloads = new List<object?> { null };
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            payloads.Insert(0, new { instance_id = modelId });
            payloads.Add(new { model = modelId });
            payloads.Add(new { identifier = modelId });
            payloads.Add(new { model = modelId, identifier = modelId });
        }

        foreach (var url in urls)
        {
            foreach (var payload in payloads)
            {
                try
                {
                    using HttpResponseMessage response = payload is null
                        ? await _httpClient.PostAsync(url, content: null, cancellationToken)
                        : await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Try next variant.
                }
            }
        }

        return false;
    }

    public async Task<string> SummarizeTextAsync(
        string modelId,
        string text,
        string language = "English",
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _options.LmStudioBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/v1/chat/completions";

        var prompt = $"""
        Summarize this trading analysis result in {language}.
        Keep it concise and decision-focused.

        Output format:
        1) Verdict (1 line)
        2) Key Bull Points (3 bullets)
        3) Key Bear/Risk Points (3 bullets)
        4) Action Plan (entry/size/risk) (3 bullets)

        Analysis content:
        {text}
        """;

        var payload = new
        {
            model = modelId,
            temperature = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are a trading assistant focused on concise, actionable summaries. Explain trading terms and ticker abbreviations briefly in glossary style where relevant."
                },
                new { role = "user", content = prompt },
            },
        };

        using var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LM Studio summarize failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("LM Studio returned no choices.");
        }

        var first = choices[0];
        if (first.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var contentProp))
        {
            if (contentProp.ValueKind == JsonValueKind.String)
            {
                return contentProp.GetString() ?? string.Empty;
            }
            if (contentProp.ValueKind == JsonValueKind.Array)
            {
                var parts = contentProp.EnumerateArray()
                    .Select(x => x.TryGetProperty("text", out var t) ? t.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                return string.Join(" ", parts!);
            }
        }

        throw new InvalidOperationException("LM Studio response did not contain assistant content.");
    }

    private static Dictionary<string, object> BuildLoadPayload(
        string modelId,
        int? contextLength,
        int? maxConcurrent,
        bool includeIdentifier)
    {
        var map = new Dictionary<string, object>
        {
            ["model"] = modelId,
        };

        if (includeIdentifier)
        {
            map["identifier"] = modelId;
        }

        if (contextLength.HasValue && contextLength.Value > 0)
        {
            map["context_length"] = contextLength.Value;
        }
        if (maxConcurrent.HasValue && maxConcurrent.Value > 0)
        {
            map["num_experts"] = maxConcurrent.Value;
        }

        return map;
    }

    private static List<LmStudioModelInfo> ParseModels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement data;

        // Common shapes:
        // 1) { "data": [ { "id": "..." } ] } (OpenAI-compatible)
        // 2) { "models": [ ... ] }
        // 3) [ ... ] (plain array)
        if (root.ValueKind == JsonValueKind.Array)
        {
            data = root;
        }
        else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
        {
            data = dataProp;
        }
        else if (root.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
        {
            data = modelsProp;
        }
        else
        {
            return [];
        }

        var models = new List<LmStudioModelInfo>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    models.Add(new LmStudioModelInfo(value, false));
                }
                continue;
            }

            var id = TryReadModelId(item);
            if (!string.IsNullOrWhiteSpace(id))
            {
                models.Add(new LmStudioModelInfo(id, IsLoadedModel(item)));
            }
        }

        return models
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LmStudioModelInfo(g.Key, g.Any(x => x.IsLoaded)))
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryReadModelId(JsonElement item)
    {
        if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
        {
            return idProp.GetString();
        }
        if (item.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String)
        {
            return modelProp.GetString();
        }
        if (item.TryGetProperty("modelKey", out var modelKeyProp) && modelKeyProp.ValueKind == JsonValueKind.String)
        {
            return modelKeyProp.GetString();
        }
        if (item.TryGetProperty("model_key", out var modelKeySnakeProp) && modelKeySnakeProp.ValueKind == JsonValueKind.String)
        {
            return modelKeySnakeProp.GetString();
        }
        return null;
    }

    private static bool IsLoadedModel(JsonElement item)
    {
        if (item.TryGetProperty("loaded", out var loadedProp) && loadedProp.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        if (item.TryGetProperty("isLoaded", out var isLoadedProp) && isLoadedProp.ValueKind == JsonValueKind.True)
        {
            return true;
        }
        if (item.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
        {
            var state = stateProp.GetString();
            if (!string.IsNullOrWhiteSpace(state) && state.Equals("loaded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        if (item.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.String)
        {
            var status = statusProp.GetString();
            if (!string.IsNullOrWhiteSpace(status) && status.Contains("loaded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
