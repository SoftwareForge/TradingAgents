namespace TradingManager.Components.Models;

public class RunSnapshotDto
{
    public string JobId { get; set; } = string.Empty;
    public string? Ticker { get; set; }
    public string? AnalysisDate { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? ResearchDepth { get; set; }
    public string? Language { get; set; }
    public string? ReportVerbosity { get; set; }
    public int? ContextLength { get; set; }
    public int? MaxConcurrent { get; set; }
    public string? Status { get; set; }
    public int Progress { get; set; }
    public string? QueueInfo { get; set; }
    public string? Summary { get; set; }
    public string? RawStatusJson { get; set; }
    public string? RawResultJson { get; set; }
    public List<PersistedMetricDto> Metrics { get; set; } = [];
    public List<PersistedSignalDto> Signals { get; set; } = [];
}