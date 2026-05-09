namespace TradingManager.Components.Models;

public class ModelListResponse
{
    public IEnumerable<string>? Models { get; set; }
    public string? LoadedModelId { get; set; }
}