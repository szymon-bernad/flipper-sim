using System.Text.Json.Nodes;

namespace FlipperSimLib;

public record RealEstateOffer
{
    public string PropertyRefId { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public decimal UsableAreaSqMeters { get; init; }
    public decimal OfferPricePerSqMeter { get; init; }
    public bool IsPremium { get; init; }
    public long CreatedAt { get; init; }

    public decimal TotalPrice() => UsableAreaSqMeters * OfferPricePerSqMeter;

    #region Serialization

    public JsonObject ToJsonObject()
    {
        return new JsonObject
        {
            ["propertyRefId"] = PropertyRefId,
            ["address"] = Address,
            ["usableAreaSqMeters"] = UsableAreaSqMeters,
            ["offerPricePerSqMeter"] = OfferPricePerSqMeter,
            ["isPremium"] = IsPremium,
            ["createdAt"] = CreatedAt
        };
    }

    public static RealEstateOffer FromJsonObject(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new RealEstateOffer
        {
            PropertyRefId = GetRequiredValue<string>(json, "propertyRefId"),
            Address = GetRequiredValue<string>(json, "address"),
            UsableAreaSqMeters = GetRequiredValue<decimal>(json, "usableAreaSqMeters"),
            OfferPricePerSqMeter = GetRequiredValue<decimal>(json, "offerPricePerSqMeter"),
            IsPremium = GetRequiredValue<bool>(json, "isPremium"),
            CreatedAt = GetRequiredValue<long>(json, "createdAt")
        };
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
}