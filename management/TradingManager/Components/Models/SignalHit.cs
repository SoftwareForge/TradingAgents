namespace TradingManager.Components.Models;

public class SignalHit
{
    public string Signal { get; set; } = "";
    public string Source { get; set; } = "";
    public string Context { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}