namespace TradingManager.Options;

public sealed class ManagerOptions
{
    public string PythonExe { get; set; } = "python";
    public bool UseConda { get; set; } = true;
    public string CondaExePath { get; set; } = @"C:\Users\michl\miniconda3\Scripts\conda.exe";
    public string CondaEnvName { get; set; } = "tradingagents";
    public string TradingAgentsApiModule { get; set; } = "tradingagents_api.main";
    public string TradingAgentsApiHost { get; set; } = "127.0.0.1";
    public int TradingAgentsApiPort { get; set; } = 8081;
    public string? TradingAgentsRepoPath { get; set; }
    public string LmStudioBaseUrl { get; set; } = "http://127.0.0.1:1234";
}
