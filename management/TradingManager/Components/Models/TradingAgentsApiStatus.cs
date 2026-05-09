namespace TradingManager.Components.Models;

public class TradingAgentsApiStatus
{
    public bool IsRunning { get; set; }
    public int? Pid { get; set; }
    public string? BaseUrl { get; set; }
}