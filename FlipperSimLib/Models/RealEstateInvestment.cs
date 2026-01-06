using System.Runtime.CompilerServices;

namespace FlipperSimLib.Models;

public class RealEstateInvestment(IMarketPriceProvider _provider)
{
    public string PropertyRefId { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public decimal PurchasePrice { get; set; } = 1.0m;

    public decimal UsableAreaSqMeters { get; init; }

    public bool IsPremium { get => _isPremium; init => _isPremium = value; }

    public bool IsBeingUpgraded { get => _upgradeInProgress; init => _upgradeInProgress = value; }

    public bool IsBeingRented => _isBeingRented;

    public decimal GetCurrentPrice() => Math.Round(UsableAreaSqMeters * _provider.GetMarketPricePerSqMeter(UsableAreaSqMeters, _isPremium), 2, MidpointRounding.AwayFromZero);

    public decimal GetPaymentAmount(long updateCounter) => (updateCounter % _paymentFrequency == 0) ?
        Math.Round(GetCurrentPrice() * 0.00444m, 2, MidpointRounding.AwayFromZero) :
        0.0m;

    public void Upgrade(decimal upgradeFee, long updateCounter)
    {
        _isPremium = true;
        _upgradeInProgress = true;
        _upgradeToBeFinishedAt = updateCounter + 20;
        PurchasePrice += upgradeFee;
    }

    public void CheckUpgradeProgress(long updateCounter)
    {
        if (_upgradeInProgress && _upgradeToBeFinishedAt <= updateCounter)
        {
            _upgradeInProgress = false;
            _upgradeToBeFinishedAt = 0;
        }
    }

    public void RentProperty(int paymentFrequency = 5)
    {
        _isBeingRented = true;
        _paymentFrequency = paymentFrequency;
    }

    public void EndRenting()
    {
        _isBeingRented = false;
    }

    private bool _isPremium;

    private bool _upgradeInProgress;

    private bool _isBeingRented;

    private long _upgradeToBeFinishedAt = 0;

    private int _paymentFrequency = 5;
}
