namespace FlipperSimLib;

public class MarketPriceProvider : IMarketPriceProvider, IMarketPriceGenerator
{
    public MarketPriceProvider(int? rndSeed = null)
    {
        _rndInstance = new Random(rndSeed ?? (int)DateTime.Now.Ticks);
        _marketPriceFactor = 1.0m + ((decimal)_rndInstance.NextDouble() - 0.5m) * 0.1987m;
    }

    public decimal GetInterestRate() => 
        Math.Round(0.005m * _marketPriceFactor, 4);

    public decimal GetMarketPricePerSqMeter(decimal area, bool isPremium)
    {
        var areaFactor = 1.0m - Math.Max(0.0m, (area - 50.0m) / 350m);
        var premiumFactor = _marketPriceFactor + (isPremium ? 0.3m : 0m);
        var pricePerSqMeter = areaFactor * premiumFactor * StdPricePerSqMeter;

        return Math.Round(pricePerSqMeter, 2);
    }


    public void RunGenerator()
    {
        if (--_nextFactorUpdateIn <= 0)
        {
            _factorTrendDirection = (decimal)(_rndInstance.NextDouble() - 0.5) * 0.05274m;
            _nextFactorUpdateIn = _rndInstance.Next(7, 21);
        }

        var noiseFactor = 0.001101m * (decimal)(_rndInstance.NextDouble() - 0.5);
        _marketPriceFactor = (1m + _factorTrendDirection + noiseFactor) * _marketPriceFactor;
        _marketPriceFactor = Math.Max(0.5m, Math.Min(2.333m, _marketPriceFactor));

        if (_marketPriceFactor == 0.5m || _marketPriceFactor == 2.333m)
        {
            _factorTrendDirection = -(2*_factorTrendDirection);
        }
    }


    private const decimal StdPricePerSqMeter = 9999m;

    private readonly Random _rndInstance;

    private decimal _marketPriceFactor = 1.0m;

    private long _nextFactorUpdateIn = 5;

    private decimal _factorTrendDirection = 0.0m;
}
