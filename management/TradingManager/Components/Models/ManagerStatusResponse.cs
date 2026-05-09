namespace TradingManager.Components.Models;

public class ManagerStatusResponse
{
    public TradingAgentsApiStatus TradingAgentsApi { get; set; } = new();
    public string? SelectedModel { get; set; }
    public string? SmallModel { get; set; }
    public string? LargeModel { get; set; }
}