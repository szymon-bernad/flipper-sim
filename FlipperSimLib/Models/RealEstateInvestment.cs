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

    public decimal GetCurrentPrice() => Math.Round(UsableAreaSqMeters * _provider.GetMarketPricePerSqMeter(UsableAreaSqMeters, _isPremium), 2, MidpointRounding.AwayFromZero);

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

    private bool _isPremium;

    private bool _upgradeInProgress;

    private long _upgradeToBeFinishedAt = 0;
}
