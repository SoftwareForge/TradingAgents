using System.Threading;

namespace TradingManager.Services;

public sealed class ModelSelectionState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _modelId;

    public async Task SetModelAsync(string? modelId)
    {
        await _gate.WaitAsync();
        try
        {
            _modelId = modelId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetModelAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _modelId;
        }
        finally
        {
            _gate.Release();
        }
    }
}
