namespace TradingManager.Components.Models;

public class PersistedMetricDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? UpdatedAt { get; set; }
    public int Confidence { get; set; }
}