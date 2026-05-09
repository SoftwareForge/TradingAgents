namespace TradingManager.Components.Pages;

public partial class Home
{
    private static string? ExtractQueueInfo(System.Text.Json.JsonElement root)
    {
        if (root.TryGetProperty("queue_position", out var queuePosition))
        {
            if (queuePosition.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                return $"position {queuePosition.GetInt32()}";
            }
            var asText = queuePosition.GetString();
            if (!string.IsNullOrWhiteSpace(asText))
            {
                return $"position {asText}";
            }
        }

        if (root.TryGetProperty("status", out var statusProp))
        {
            var status = statusProp.GetString();
            if (string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase))
            {
                return "queued";
            }
        }

        return null;
    }

    private string FormatElapsed()
    {
        var start = _runStartedAtUtc ?? DateTime.UtcNow;
        var span = DateTime.UtcNow - start;
        if (span.TotalSeconds < 0)
        {
            span = TimeSpan.Zero;
        }
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }

    private static string FormatToken(int value)
    {
        if (value >= 1_000_000) return $"{value / 1_000_000d:0.#}m";
        if (value >= 1_000) return $"{value / 1_000d:0.#}k";
        return value.ToString();
    }

    private static int MapResearchDepth(string label) => label switch
    {
        "Shallow" => 1,
        "Medium" => 3,
        "Deep" => 5,
        _ => 3,
    };

    private static int MapContextLength(string label) => label switch
    {
        "32k" => 32_768,
        "64k" => 65_536,
        "128k" => 131_072,
        "256k" => 262_144,
        _ => 131_072,
    };
}
