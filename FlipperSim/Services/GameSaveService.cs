using FlipperSimLib;
using Microsoft.JSInterop;

namespace FlipperSim.Services;

public class GameSaveService(IJSRuntime js)
{
    private const string SaveKey = "FlipperSim.SavedGame";

    public async Task SaveGameAsync(RealEstateMarketSimulation simulation)
    {
        var json = simulation.ToJsonString();
        await js.InvokeVoidAsync("gameStorage.save", SaveKey, json);
    }

    public async Task<RealEstateMarketSimulation?> LoadGameAsync(
        IMarketPriceGenerator marketPriceGen,
        IRandomProvider randomProvider,
        SimulationConfig? config = null)
    {
        var json = await js.InvokeAsync<string?>("gameStorage.load", SaveKey);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return RealEstateMarketSimulation.FromJsonString(json, marketPriceGen, randomProvider, config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load saved game: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> HasSavedGameAsync()
    {
        return await js.InvokeAsync<bool>("gameStorage.exists", SaveKey);
    }

    public async Task DeleteSaveAsync()
    {
        await js.InvokeVoidAsync("gameStorage.remove", SaveKey);
    }
}