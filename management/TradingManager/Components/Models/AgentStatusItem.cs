namespace TradingManager.Components.Models;

public class AgentStatusItem(string agentName)
{
    public string AgentName { get; } = agentName;
    public string Status { get; set; } = "pending";
}