namespace TradingManager.Components.Models;

public class PersistedSignalDto
{
    public string Signal { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}