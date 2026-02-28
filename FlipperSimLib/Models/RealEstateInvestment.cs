using System.Reflection.Metadata.Ecma335;
using System.Text.Json.Nodes;

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

    private const decimal RentalRatePerCycle = 0.00255m;

    public decimal GetPaymentAmount(long updateCounter)
    {
        if (updateCounter % _paymentFrequency != 0)
        {
            return 0m;
        }
        
        _previousRentAmount = GetRentAmount();
        return _previousRentAmount;
    }

    public decimal GetEstimatedRentalIncomePerCycle() => GetRentAmount();

    public void Upgrade(decimal upgradeFee, long updateCounter)
    {
        _isPremium = true;
        _upgradeInProgress = true;
        _upgradeToBeFinishedAt = updateCounter + 16;
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

    public void RentProperty(int paymentFrequency = 4)
    {
        _isBeingRented = true;
        _paymentFrequency = paymentFrequency;
    }

    public void EndRenting()
    {
        _isBeingRented = false;
    }

    #region Serialization

    public JsonObject ToJsonObject()
    {
        return new JsonObject
        {
            ["propertyRefId"] = PropertyRefId,
            ["address"] = Address,
            ["purchasePrice"] = PurchasePrice,
            ["usableAreaSqMeters"] = UsableAreaSqMeters,
            ["isPremium"] = _isPremium,
            ["isBeingUpgraded"] = _upgradeInProgress,
            ["upgradeToBeFinishedAt"] = _upgradeToBeFinishedAt,
            ["isBeingRented"] = _isBeingRented,
            ["paymentFrequency"] = _paymentFrequency
        };
    }

    public static RealEstateInvestment FromJsonObject(JsonObject json, IMarketPriceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(provider);

        var investment = new RealEstateInvestment(provider)
        {
            PropertyRefId = GetRequiredValue<string>(json, "propertyRefId"),
            Address = GetRequiredValue<string>(json, "address"),
            PurchasePrice = GetRequiredValue<decimal>(json, "purchasePrice"),
            UsableAreaSqMeters = GetRequiredValue<decimal>(json, "usableAreaSqMeters"),
            IsPremium = GetRequiredValue<bool>(json, "isPremium"),
            IsBeingUpgraded = GetRequiredValue<bool>(json, "isBeingUpgraded")
        };

        investment._upgradeToBeFinishedAt = GetRequiredValue<long>(json, "upgradeToBeFinishedAt");
        investment._isBeingRented = GetRequiredValue<bool>(json, "isBeingRented");
        investment._paymentFrequency = GetRequiredValue<int>(json, "paymentFrequency");

        return investment;
    }

    private static T GetRequiredValue<T>(JsonObject json, string propertyName)
    {
        var node = json[propertyName]
            ?? throw new InvalidOperationException($"Required property '{propertyName}' is missing.");

        try
        {
            return node.GetValue<T>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' has invalid value. Expected type: {typeof(T).Name}.", ex);
        }
    }

    #endregion


    private decimal GetRentAmount() =>
        _previousRentAmount == 0m ?
            GetCurrentPrice() * RentalRatePerCycle :
            (_previousRentAmount * 0.7m) + (GetCurrentPrice() * RentalRatePerCycle * 0.3m);

    private bool _isPremium;

    private bool _upgradeInProgress;

    private bool _isBeingRented;

    private long _upgradeToBeFinishedAt = 0;

    private int _paymentFrequency;

    private decimal _previousRentAmount = 0m;
}
