namespace FlipperSimLib.Tests.TestDoubles;

public class FakeMarketPriceProvider : IMarketPriceProvider
{
    public Func<decimal, bool, decimal>? PriceResolver { get; set; }

    public decimal DefaultPricePerSqMeter { get; set; } = 1_000m;

    public decimal InterestRate { get; set; } = 0.01m;

    public decimal GetMarketPricePerSqMeter(decimal area, bool isPremium) =>
        PriceResolver?.Invoke(area, isPremium) ?? DefaultPricePerSqMeter;

    public decimal GetInterestRate() => InterestRate;
}