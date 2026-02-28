using FlipperSim.Models;
using FlipperSimLib;
using FlipperSimLib.Models;
using Radzen.Blazor.Markdown;
using System.Diagnostics;

namespace FlipperSim.Services;

public sealed class GameLoopService
{
    private readonly GameSaveService _gameSaveService;
    private readonly MarketPriceProvider _priceProvider = new();
    private RealEstateMarketSimulation _marketSim;

    // Timer and loop state
    private Timer? _refreshTimer;
    private readonly Stopwatch _stopWatch = new();
    private bool _isPaused;
    private bool _isGameOver;
    private string _gameOverReason = string.Empty;

    // Chart data
    private readonly Queue<ChartDataPoint> _chartValues = new ();

    // Current offers snapshot
    private ICollection<RealEstateOffer> _currentOffers = [];

    public GameLoopService(GameSaveService gameSaveService)
    {
        _gameSaveService = gameSaveService;
        _marketSim = new RealEstateMarketSimulation(_priceProvider, null);
    }

    // ══════════════════════════════════════════════
    //  READ-ONLY STATE (UI binds to these)
    // ══════════════════════════════════════════════

    public RealEstateMarketSimulation MarketSim => _marketSim;
    public ICollection<RealEstateOffer> CurrentOffers => _currentOffers;
    public ICollection<ChartDataPoint> ChartValues => _chartValues.ToList();
    public bool IsPaused => _isPaused;
    public decimal LoansTotal => _marketSim.FlipperAccount.Loans.Sum(l => l.LoanAmount);
    public decimal InvestmentsWorth => _marketSim.FlipperAccount.Investments.Sum(i => i.GetCurrentPrice());
    public decimal LoanCapacity => Math.Max(
        350_000m,
        0.6m * (_marketSim.FlipperAccount.AccountBalance - LoansTotal + InvestmentsWorth));

    // ══════════════════════════════════════════════
    //  EVENTS
    // ══════════════════════════════════════════════

    /// <summary>
    /// Fired after every game tick and every game action.
    /// UI should call InvokeAsync(StateHasChanged).
    /// </summary>
    public event Func<Task>? OnStateChanged;

    /// <summary>
    /// Fired when game-over is detected. The tick awaits this delegate
    /// so the UI can show a blocking dialog before the tick continues.
    /// After the dialog is dismissed, the tick clears the game-over flag
    /// and proceeds (which triggers the welcome dialog for the fresh game).
    /// </summary>
    public event Func<string, Task>? OnGameOver;

    /// <summary>
    /// Fired when a fresh game is detected (after reset or first launch).
    /// The tick awaits this delegate so the welcome dialog blocks the loop.
    /// </summary>
    public event Func<Task>? OnFreshGame;

    // ══════════════════════════════════════════════
    //  LIFECYCLE
    // ══════════════════════════════════════════════

    public async Task<bool> HasSavedGameAsync()
    {
        return await _gameSaveService.HasSavedGameAsync();
    }

    public async Task LoadSavedGameAsync()
    {
        var loaded = await _gameSaveService.LoadGameAsync(
            _priceProvider, new SystemRandomProvider(null));
        if (loaded is not null)
        {
            _marketSim = loaded;
        }
    }

    public async Task DeleteSavedGameAsync()
    {
        await _gameSaveService.DeleteSaveAsync();
    }

    /// <summary>
    /// Starts the game loop timer. Call after culture is set
    /// and the saved-game decision is resolved.
    /// </summary>
    public void StartGameLoop()
    {
        if (_refreshTimer is null)
        {
            _refreshTimer = new Timer(
                async s => await TickAsync(s), null, 0, Timeout.Infinite);
        }
    }

    public async Task PauseAndSaveAsync()
    {
        _isPaused = true;
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        await _gameSaveService.SaveGameAsync(_marketSim);

        OnStateChanged?.Invoke();
    }

    public async Task Resume()
    {
        _isPaused = false;
        _refreshTimer?.Change(0, Timeout.Infinite);
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task DeleteOffer(RealEstateOffer offer)
    {
        _marketSim.DeleteOffer(offer.PropertyRefId);
        _currentOffers.Remove(offer);
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task BuyOffer(RealEstateOffer offer)
    {
        try
        {
            _marketSim.BuyOffer(offer.PropertyRefId);
            _currentOffers.Remove(offer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error buying offer {offer.PropertyRefId}: {ex.Message}");
        }
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task SellInvestment(string refId)
    {
        try
        {
            _marketSim.SellInvestment(refId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error selling investment {refId}: {ex.Message}");
        }
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task TakeLoan(decimal amount)
    {
        try
        {
            _marketSim.FlipperAccount.AddLoan(new Loan
            {
                InterestRate = _marketSim.GetInterestRate(),
                LoanAmount = amount,
                PaymentFrequency = 4,
                LoanId = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error taking loan of {amount}: {ex.Message}");
        }
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task PayOffLoan(string loanId)
    {
        try
        {
            _marketSim.FlipperAccount.PayOffLoan(loanId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error paying off loan {loanId}: {ex.Message}");
        }
        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
    }

    public async Task UpgradeInvestment(string refId)
    {
        if (_marketSim.UpgradeInvestment(refId))
        {
            await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
        }
    }

    public async Task ToggleInvestmentRenting(string refId)
    {
        if (_marketSim.ToggleInvestmentRenting(refId))
        {
            await (OnStateChanged?.Invoke() ?? Task.CompletedTask);
        }
    }

    private async Task TickAsync(object? state)
    {
        if (_isPaused)
        {
            return;
        }

        _stopWatch.Restart();

        // Game-over handling (from previous tick's detection).
        if (_isGameOver)
        {
            _marketSim.ResetAccount();
            await _gameSaveService.DeleteSaveAsync();

            await (OnGameOver?.Invoke(_gameOverReason) ?? Task.CompletedTask);

            _isGameOver = false;
        }

        // Fresh game detection (fires after reset or on first launch)
        if (_marketSim.IsFreshGame)
        {
            if (OnFreshGame is not null)
            {
                await OnFreshGame.Invoke();
            }
        }

        // Core market update
        _currentOffers = [.. _marketSim
            .GetRealEstateOffersAfterUpdates(10)
            .OrderBy(o => o.CreatedAt)];

        // Update chart
        _chartValues.Enqueue(new ChartDataPoint(
            _chartValues.LastOrDefault()?.Index + 1 ?? 1,
            (double)_marketSim.GetMarketPricePerSqMeter(50.0m, false)));

        if (_chartValues.Count > 100)
        {
            _chartValues.Dequeue();
        }

        // Check game over (result is acted on next tick)
        (_isGameOver, _gameOverReason) = _marketSim.CheckIfGameIsOver();

        await (OnStateChanged?.Invoke() ?? Task.CompletedTask);

        _stopWatch.Stop();
        var dt = Math.Max(0, 1600 - _stopWatch.ElapsedMilliseconds);
        _refreshTimer?.Change(dt, Timeout.Infinite);
    }
}
