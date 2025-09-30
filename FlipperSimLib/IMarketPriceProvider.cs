namespace FlipperSimLib;

public interface IMarketPriceProvider
{
    public decimal GetMarketPricePerSqMeter(decimal area, bool isPremium);

    public decimal GetInterestRate();
}
