namespace FlipperSimLib.Models;

public record RealEstateOffer
{
    public string PropertyRefId { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public decimal UsableAreaSqMeters { get; init; } = 1.0m;
    public decimal OfferPricePerSqMeter { get; init; } = 1.0m;

    public bool IsPremium { get; init; } = false;

    public long CreatedAt { get; init; } = 0;

    public decimal TotalPrice() => Math.Round(UsableAreaSqMeters * OfferPricePerSqMeter, 2, MidpointRounding.ToEven);
}
