using FlipperSimLib.Tests.Builders;
using FlipperSimLib.Tests.TestDoubles;

namespace FlipperSimLib.Tests;

public sealed class FlipperAccountTests
{
    private readonly FakeMarketPriceProvider _priceProvider = new();

    [Fact]
    public void AddLoan_IncreasesBalance_AndStoresLoan()
    {
        var account = new FlipperAccount(100_000m, _priceProvider);
        var loan = new LoanBuilder().WithAmount(50_000m).Build();

        account.AddLoan(loan);

        Assert.Equal(150_000m, account.AccountBalance);
        Assert.Contains(account.Loans, l => l.LoanId == loan.LoanId);
    }

    [Fact]
    public void PayOffLoan_RemovesLoan_WhenFundsAvailable()
    {
        var loan = new LoanBuilder().WithAmount(20_000m).Build();
        var account = new FlipperAccount(50_000m, _priceProvider);
        account.AddLoan(loan);

        account.PayOffLoan(loan.LoanId);

        Assert.Equal(50_000m, account.AccountBalance);
        Assert.Empty(account.Loans);
    }

    [Fact]
    public void AddInvestment_DebitsBalance_AndCreatesInvestment()
    {
        var offer = new RealEstateOffer
        {
            UsableAreaSqMeters = 40m,
            OfferPricePerSqMeter = 2_000m,
        };
        var account = new FlipperAccount(200_000m, _priceProvider);

        account.AddInvestment(offer);

        var investment = Assert.Single(account.Investments);
        Assert.Equal(offer.PropertyRefId, investment.PropertyRefId);
        Assert.Equal(200_000m - offer.TotalPrice(), account.AccountBalance);
    }
}