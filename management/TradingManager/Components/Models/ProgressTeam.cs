namespace TradingManager.Components.Models;

public class ProgressTeam(string teamName, List<AgentStatusItem> agents)
{
    public string TeamName { get; } = teamName;
    public List<AgentStatusItem> Agents { get; } = agents;
}