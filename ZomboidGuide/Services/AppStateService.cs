using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class AppStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private readonly string _stateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZomboidGuide",
        "state.json");

    public async Task<AppState> LoadAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AppState();
            }

            await using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions);
            return state ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _saveGate.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_stateFilePath);
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
