using System.Threading;

namespace TradingManager.Services;

public sealed class ModelSelectionState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _smallModelId;
    private string? _largeModelId;

    public async Task SetModelsAsync(string? smallModelId, string? largeModelId)
    {
        await _gate.WaitAsync();
        try
        {
            _smallModelId = smallModelId;
            _largeModelId = largeModelId;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SetModelAsync(string? modelId)
    {
        // Backward compatible single-model assignment.
        return SetModelsAsync(modelId, modelId);
    }

    public async Task SetModelForRoleAsync(string role, string? modelId)
    {
        await _gate.WaitAsync();
        try
        {
            if (string.Equals(role, "small", StringComparison.OrdinalIgnoreCase))
            {
                _smallModelId = modelId;
            }
            else if (string.Equals(role, "large", StringComparison.OrdinalIgnoreCase))
            {
                _largeModelId = modelId;
            }
            else
            {
                _smallModelId = modelId;
                _largeModelId = modelId;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(string? SmallModelId, string? LargeModelId)> GetModelsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return (_smallModelId, _largeModelId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetModelAsync()
    {
        var models = await GetModelsAsync();
        return models.LargeModelId ?? models.SmallModelId;
    }
}
