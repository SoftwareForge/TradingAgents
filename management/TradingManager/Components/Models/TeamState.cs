namespace TradingManager.Components.Models;

public class TeamState
{
    public string Team { get; set; } = "System";
    public string Agent { get; set; } = "System";
    public string Stage { get; set; } = "";
    public string Phase { get; set; } = "";
    public int Progress { get; set; }
}