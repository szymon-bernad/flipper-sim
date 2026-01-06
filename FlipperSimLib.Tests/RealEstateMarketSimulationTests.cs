using FlipperSimLib.Tests.Builders;
using FlipperSimLib.Tests.TestDoubles;

namespace FlipperSimLib.Tests;

public sealed class RealEstateMarketSimulationTests
{
    [Fact]
    public void GetUpdatedRealEstateOffers_RefreshesMarketplace_Statefully()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var expectedSimulationUpdates = 1;
        var expectedPriceGeneratorRuns = 1;

        var expectedMarketOfferCount = 3;
        var expectedNewOffersCount = 2;
        var expectedPremiumArea = 28.10m;
        var expectedPremiumPricePerSqMeter = 1_975m;
        var expectedStandardArea = 43.25m;
        var expectedStandardPricePerSqMeter = 1_259m;

        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, isPremium) => isPremium ? 2_000m : 1_200m,
        };
        var random = new FakeRandomProvider();
        random.Enqueue(0.1, 0.8, 0.4, 0.25, 0.5, 0.9);

        var staleOffer = new RealEstateOffer
        {
            PropertyRefId = "stale",
            CreatedAt = -50,
            UsableAreaSqMeters = 35m,
            OfferPricePerSqMeter = 1_100m,
        };

        var activeOffer = new RealEstateOffer
        {
            PropertyRefId = "active",
            CreatedAt = 1,
            UsableAreaSqMeters = 60m,
            OfferPricePerSqMeter = 1_500m,
        };

        var expectedUpdatedOfferPrice = Math.Round(
                (config.OfferPriceRetentionWeight * activeOffer.OfferPricePerSqMeter) +
                (config.MarketPriceInfluenceWeight * priceGen.GetMarketPricePerSqMeter(activeOffer.UsableAreaSqMeters, activeOffer.IsPremium)),
                2);

        var offers = new List<RealEstateOffer> { staleOffer, activeOffer };

        var account = new FlipperAccount(config.DefaultAccountBalance, priceGen);
        var loan = new LoanBuilder()
            .WithAmount(10_000m)
            .WithInterestRate(0.01m)
            .WithPaymentFrequency(1)
            .Build();
        account.AddLoan(loan);

        var rentalOffer = new RealEstateOffer
        {
            PropertyRefId = "rent",
            CreatedAt = 0,
            OfferPricePerSqMeter = 1_000m,
            UsableAreaSqMeters = 25m,
        };

        account.AddInvestment(rentalOffer);
        var rental = account.Investments.Single(i => i.PropertyRefId == rentalOffer.PropertyRefId);
        rental.RentProperty(1);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 0,
            initialOffers: offers,
            flipperAccount: account,
            config: config);

        var nextUpdateCounter = simulation.UpdatesCounter + 1;
        var expectedBalanceDelta =
            account.Investments.Sum(investment => investment.GetPaymentAmount(nextUpdateCounter)) -
            account.Loans.Sum(l => l.GetPaymentAmount(nextUpdateCounter));

        var startingBalance = account.AccountBalance;

        // Act
        var result = simulation.GetRealEstateOffersAfterUpdates(3).ToList();

        // Assert
        Assert.Equal(expectedSimulationUpdates, simulation.UpdatesCounter);
        Assert.Equal(expectedPriceGeneratorRuns, priceGen.RunCount);
        Assert.Equal(startingBalance + expectedBalanceDelta, account.AccountBalance);

        Assert.DoesNotContain(result, o => o.PropertyRefId == staleOffer.PropertyRefId);

        var updatedOffer = Assert.Single(result, o => o.PropertyRefId == activeOffer.PropertyRefId);
        Assert.Equal(expectedUpdatedOfferPrice, updatedOffer.OfferPricePerSqMeter);

        Assert.Equal(expectedMarketOfferCount, result.Count);
        var newOffers = offers.Where(o => o.PropertyRefId != activeOffer.PropertyRefId).ToList();
        Assert.Equal(expectedNewOffersCount, newOffers.Count);

        var premiumOffer = Assert.Single(newOffers.Where(o => o.IsPremium));
        Assert.Equal(expectedPremiumArea, premiumOffer.UsableAreaSqMeters);
        Assert.Equal(expectedPremiumPricePerSqMeter, premiumOffer.OfferPricePerSqMeter);

        var standardOffer = Assert.Single(newOffers.Where(o => !o.IsPremium));
        Assert.Equal(expectedStandardArea, standardOffer.UsableAreaSqMeters);
        Assert.Equal(expectedStandardPricePerSqMeter, standardOffer.OfferPricePerSqMeter);
    }

    [Fact]
    public void BuyOffer_RemovesListing_AndCreatesInvestment()
    {
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => 1_500m,
        };
        var random = new FakeRandomProvider();

        var targetOffer = new RealEstateOffer
        {
            PropertyRefId = "target",
            UsableAreaSqMeters = 30m,
            OfferPricePerSqMeter = 2_000m,
        };

        var remainingOffer = new RealEstateOffer
        {
            PropertyRefId = "other",
            UsableAreaSqMeters = 25m,
            OfferPricePerSqMeter = 1_500m,
        };

        var offers = new List<RealEstateOffer> { targetOffer, remainingOffer };
        var account = new FlipperAccount(500_000m, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            initialOffers: offers,
            flipperAccount: account);

        simulation.BuyOffer(targetOffer.PropertyRefId);

        Assert.DoesNotContain(offers, o => o.PropertyRefId == targetOffer.PropertyRefId);
        Assert.Contains(offers, o => o.PropertyRefId == remainingOffer.PropertyRefId);

        var investment = Assert.Single(account.Investments, i => i.PropertyRefId == targetOffer.PropertyRefId);
        Assert.Equal(targetOffer.Address, investment.Address);
        Assert.Equal(500_000m - targetOffer.TotalPrice(), account.AccountBalance);
    }

    [Fact]
    public void SellInvestment_RemovesInvestment_AndCreditsAccountBalance()
    {
        // Arrange
        var marketPricePerSqMeter = 1_800m;
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => marketPricePerSqMeter,
        };
        var random = new FakeRandomProvider();

        var initialBalance = 200_000m;
        var account = new FlipperAccount(initialBalance, priceGen);

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "sell-target",
            UsableAreaSqMeters = 50m,
            OfferPricePerSqMeter = 1_500m,
        };

        account.AddInvestment(propertyOffer);
        var investment = account.Investments.Single();
        var expectedSalePrice = investment.GetCurrentPrice();
        var balanceAfterPurchase = account.AccountBalance;

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account);

        // Act
        simulation.SellInvestment(propertyOffer.PropertyRefId);

        // Assert
        Assert.Empty(account.Investments);
        Assert.Equal(balanceAfterPurchase + expectedSalePrice, account.AccountBalance);
    }

    [Fact]
    public void UpgradeInvestment_WhenSufficientFunds_DeductsUpgradeFeeAndMarksPremium()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var marketPricePerSqMeter = 2_000m;
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => marketPricePerSqMeter,
        };
        var random = new FakeRandomProvider();

        var initialBalance = 500_000m;
        var account = new FlipperAccount(initialBalance, priceGen);

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "upgrade-target",
            UsableAreaSqMeters = 40m,
            OfferPricePerSqMeter = 1_800m,
            IsPremium = false,
        };

        account.AddInvestment(propertyOffer);
        var investment = account.Investments.Single();
        var expectedUpgradeFee = Math.Round(config.UpgradeFeeRate * investment.GetCurrentPrice(), 2, MidpointRounding.AwayFromZero);
        var balanceAfterPurchase = account.AccountBalance;

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account,
            config: config);

        // Act
        var result = simulation.UpgradeInvestment(propertyOffer.PropertyRefId);

        // Assert
        Assert.True(result);
        Assert.True(investment.IsPremium);
        Assert.True(investment.IsBeingUpgraded);
        Assert.Equal(balanceAfterPurchase - expectedUpgradeFee, account.AccountBalance);
    }

    [Theory]
    [InlineData(500_000, true)]  // Sufficient funds - upgrade succeeds
    [InlineData(72_100, false)]  // Insufficient funds (just purchase price + 100) - upgrade fails
    public void UpgradeInvestment_ReturnsExpectedResult_BasedOnAvailableFunds(
        decimal initialBalance,
        bool expectedResult)
    {
        // Arrange
        var config = SimulationConfig.Default;
        var marketPricePerSqMeter = 2_000m;
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => marketPricePerSqMeter,
        };
        var random = new FakeRandomProvider();

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "upgrade-target",
            UsableAreaSqMeters = 40m,
            OfferPricePerSqMeter = 1_800m,
            IsPremium = false,
        };

        var account = new FlipperAccount(initialBalance, priceGen);
        account.AddInvestment(propertyOffer);

        var investment = account.Investments.Single();
        var expectedUpgradeFee = Math.Round(config.UpgradeFeeRate * investment.GetCurrentPrice(), 2, MidpointRounding.AwayFromZero);
        var balanceAfterPurchase = account.AccountBalance;

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account,
            config: config);

        // Act
        var result = simulation.UpgradeInvestment(propertyOffer.PropertyRefId);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedResult, investment.IsPremium);
        Assert.Equal(expectedResult, investment.IsBeingUpgraded);

        var expectedBalance = expectedResult
            ? balanceAfterPurchase - expectedUpgradeFee
            : balanceAfterPurchase;
        Assert.Equal(expectedBalance, account.AccountBalance);
    }

    [Theory]
    [InlineData(1_800)]  // Market price higher than purchase
    [InlineData(1_200)]  // Market price lower than purchase
    [InlineData(1_500)]  // Market price equal to purchase
    public void SellInvestment_RemovesInvestment_AndCreditsCurrentMarketValue(
        decimal marketPricePerSqMeter)
    {
        // Arrange
        var purchasePricePerSqMeter = 1_500m;
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => marketPricePerSqMeter,
        };
        var random = new FakeRandomProvider();

        var initialBalance = 200_000m;
        var account = new FlipperAccount(initialBalance, priceGen);

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "sell-target",
            UsableAreaSqMeters = 50m,
            OfferPricePerSqMeter = purchasePricePerSqMeter,
        };

        account.AddInvestment(propertyOffer);
        var investment = account.Investments.Single();
        var purchasePrice = propertyOffer.TotalPrice();
        var expectedSalePrice = investment.GetCurrentPrice();
        var balanceAfterPurchase = account.AccountBalance;

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account);

        // Act
        simulation.SellInvestment(propertyOffer.PropertyRefId);

        // Assert
        Assert.Empty(account.Investments);
        Assert.Equal(balanceAfterPurchase + expectedSalePrice, account.AccountBalance);

        // Verify sale price reflects market value relative to purchase price
        var expectedPriceDifference = (marketPricePerSqMeter - purchasePricePerSqMeter) * propertyOffer.UsableAreaSqMeters;
        Assert.Equal(expectedPriceDifference, expectedSalePrice - purchasePrice);
    }

    #region ToggleInvestmentRenting Tests

    [Fact]
    public void ToggleInvestmentRenting_WhenInvestmentNotFound_ReturnsFalse()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();
        var account = new FlipperAccount(config.DefaultAccountBalance, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account,
            config: config);

        // Act
        var result = simulation.ToggleInvestmentRenting("non-existent-id");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DeleteOffer Tests

    [Fact]
    public void DeleteOffer_RemovesOfferFromMarketplace()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var offerToDelete = new RealEstateOffer
        {
            PropertyRefId = "to-delete",
            UsableAreaSqMeters = 40m,
            OfferPricePerSqMeter = 1_500m,
        };

        var remainingOffer = new RealEstateOffer
        {
            PropertyRefId = "remaining",
            UsableAreaSqMeters = 50m,
            OfferPricePerSqMeter = 1_800m,
        };

        var offers = new List<RealEstateOffer> { offerToDelete, remainingOffer };

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            initialOffers: offers);

        // Act
        simulation.DeleteOffer(offerToDelete.PropertyRefId);

        // Assert
        Assert.DoesNotContain(offers, o => o.PropertyRefId == offerToDelete.PropertyRefId);
        Assert.Contains(offers, o => o.PropertyRefId == remainingOffer.PropertyRefId);
        Assert.Single(offers);
    }

    #endregion

    #region BuyOffer Tests

    [Fact]
    public void BuyOffer_WhenOfferNotFound_ThrowsArgumentException()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var offers = new List<RealEstateOffer>();
        var account = new FlipperAccount(config.DefaultAccountBalance, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            initialOffers: offers,
            flipperAccount: account,
            config: config);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            simulation.BuyOffer("non-existent-id"));

        Assert.Contains("non-existent-id", exception.Message);
        Assert.Equal("propertyRefId", exception.ParamName);
    }

    #endregion

    #region ResetAccount Tests

    [Fact]
    public void ResetAccount_CreatesNewAccountWithDefaultBalance()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var initialBalance = 500_000m;
        var account = new FlipperAccount(initialBalance, priceGen);

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "investment",
            UsableAreaSqMeters = 40m,
            OfferPricePerSqMeter = 1_500m,
        };
        account.AddInvestment(propertyOffer);

        var loan = new LoanBuilder()
            .WithAmount(50_000m)
            .WithInterestRate(0.05m)
            .Build();
        account.AddLoan(loan);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            flipperAccount: account,
            config: config);

        // Act
        simulation.ResetAccount();

        // Assert
        Assert.Equal(config.DefaultAccountBalance, simulation.FlipperAccount.AccountBalance);
        Assert.Empty(simulation.FlipperAccount.Investments);
        Assert.Empty(simulation.FlipperAccount.Loans);
    }

    #endregion

    #region IsFreshGame Tests

    [Theory]
    [InlineData(0, 0, true)]      // Both zero - fresh game
    [InlineData(100, 100, true)]  // Equal values - fresh game
    [InlineData(50, 100, true)]   // Offset greater than counter - fresh game
    [InlineData(100, 50, false)]  // Counter greater than offset - not fresh
    [InlineData(201, 0, false)]   // Counter past threshold, no offset - not fresh
    public void IsFreshGame_ReturnsCorrectValue_BasedOnOffsetAndCounter(
        long updatesCounter,
        long gameOffset,
        bool expectedIsFresh)
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: updatesCounter,
            gameOffset: gameOffset);

        // Act
        var result = simulation.IsFreshGame;

        // Assert
        Assert.Equal(expectedIsFresh, result);
    }

    #endregion

    #region GetRealEstateOffersAfterUpdates Tests

    [Fact]
    public void GetRealEstateOffersAfterUpdates_WhenMoreOffersThanRequested_ReturnsTakeCount()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => 1_500m,
        };
        var random = new FakeRandomProvider();

        var offers = new List<RealEstateOffer>
        {
            new() { PropertyRefId = "offer1", CreatedAt = 0, UsableAreaSqMeters = 30m, OfferPricePerSqMeter = 1_000m },
            new() { PropertyRefId = "offer2", CreatedAt = 0, UsableAreaSqMeters = 40m, OfferPricePerSqMeter = 1_200m },
            new() { PropertyRefId = "offer3", CreatedAt = 0, UsableAreaSqMeters = 50m, OfferPricePerSqMeter = 1_400m },
            new() { PropertyRefId = "offer4", CreatedAt = 0, UsableAreaSqMeters = 60m, OfferPricePerSqMeter = 1_600m },
            new() { PropertyRefId = "offer5", CreatedAt = 0, UsableAreaSqMeters = 70m, OfferPricePerSqMeter = 1_800m },
        };

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            initialOffers: offers);

        var requestedCount = 3;

        // Act
        var result = simulation.GetRealEstateOffersAfterUpdates(requestedCount).ToList();

        // Assert
        Assert.Equal(requestedCount, result.Count);
        Assert.Equal("offer1", result[0].PropertyRefId);
        Assert.Equal("offer2", result[1].PropertyRefId);
        Assert.Equal("offer3", result[2].PropertyRefId);
    }

    #endregion

    #region CheckIfGameIsOver Tests

    [Fact]
    public void CheckIfGameIsOver_WhenBalanceNegative_ReturnsGameOverNegativeBalance()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var initialBalance = -100m;
        var account = new FlipperAccount(0m, priceGen);
        account.HandlePayment(initialBalance); // Force negative balance

        var updatesCounter = 50L;
        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: updatesCounter,
            flipperAccount: account);

        // Act
        var (isGameOver, reason) = simulation.CheckIfGameIsOver();

        // Assert
        Assert.True(isGameOver);
        Assert.Equal("GameOverNegativeBalance", reason);
        Assert.Equal(updatesCounter, simulation.GameOffset);
    }

    [Theory]
    [InlineData(201, 0, 749_999, true)]    // Just past threshold, wealth below
    [InlineData(401, 0, 1_499_999, true)]  // Second threshold
    [InlineData(601, 0, 2_999_999, true)]  // Third threshold
    [InlineData(801, 0, 4_999_999, true)]  // Fourth threshold
    [InlineData(200, 0, 100_000, false)]   // Exactly at threshold boundary - no game over
    [InlineData(201, 0, 750_000, false)]   // Past threshold but wealth meets requirement
    public void CheckIfGameIsOver_WealthThreshold_ReturnsExpectedResult(
        long updatesCounter,
        long gameOffset,
        decimal accountBalance,
        bool expectedGameOver)
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var account = new FlipperAccount(accountBalance, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: updatesCounter,
            gameOffset: gameOffset,
            flipperAccount: account);

        // Act
        var (isGameOver, reason) = simulation.CheckIfGameIsOver();

        // Assert
        Assert.Equal(expectedGameOver, isGameOver);
        if (expectedGameOver)
        {
            Assert.Equal("GameOverNotGoodEnough", reason);
            Assert.Equal(updatesCounter, simulation.GameOffset);
        }
        else
        {
            Assert.Equal(string.Empty, reason);
        }
    }

    [Fact]
    public void CheckIfGameIsOver_WhenWealthIncludesInvestments_CalculatesCorrectly()
    {
        // Arrange
        var marketPricePerSqMeter = 10_000m;
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => marketPricePerSqMeter,
        };
        var random = new FakeRandomProvider();

        var propertyOffer = new RealEstateOffer
        {
            PropertyRefId = "valuable",
            UsableAreaSqMeters = 70m,
            OfferPricePerSqMeter = 8_000m,
        };

        // Start with 800,000 balance to afford the 560,000 purchase
        var account = new FlipperAccount(800_000m, priceGen);
        account.AddInvestment(propertyOffer);

        // Balance: 800,000 - 560,000 = 240,000
        // Investment value: 70 * 10,000 = 700,000
        // Total wealth: 240,000 + 700,000 = 940,000 > 750,000 threshold

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 201,
            flipperAccount: account);

        // Act
        var (isGameOver, reason) = simulation.CheckIfGameIsOver();

        // Assert
        Assert.False(isGameOver);
        Assert.Equal(string.Empty, reason);
    }

    [Theory]
    // Offset 0 - baseline behavior
    [InlineData(100, 0, 100_000, false)]      // Counter 100, below 200 threshold
    [InlineData(201, 0, 100_000, true)]       // Counter 201, past 200 threshold with low wealth

    // Offset 100 - first threshold shifts to 300
    [InlineData(300, 100, 100_000, false)]    // Exactly at 200+100=300, not past (uses >)
    [InlineData(301, 100, 100_000, true)]     // Past 300 threshold with low wealth
    [InlineData(301, 100, 750_000, false)]    // Past 300 threshold but wealth meets requirement

    // Offset 250 - first threshold shifts to 450
    [InlineData(450, 250, 100_000, false)]    // Exactly at 200+250=450, not past
    [InlineData(451, 250, 100_000, true)]     // Past 450 threshold with low wealth
    [InlineData(451, 250, 750_000, false)]    // Past 450 threshold, wealth meets 750K

    // Offset 500 - first threshold shifts to 700
    [InlineData(700, 500, 100_000, false)]    // Exactly at 200+500=700, not past
    [InlineData(701, 500, 100_000, true)]     // Past 700 threshold with low wealth
    [InlineData(901, 500, 750_000, true)]     // Past 400+500=900, wealth below 1.5M requirement
    [InlineData(901, 500, 1_500_000, false)]  // Past 900 threshold, wealth meets 1.5M

    // Offset 1000 - large offset, thresholds shift significantly
    [InlineData(1200, 1000, 100_000, false)]  // Counter 1200, below 200+1000=1200 (uses >)
    [InlineData(1201, 1000, 100_000, true)]   // Past 1200 threshold with low wealth
    [InlineData(1401, 1000, 750_000, true)]   // Past 400+1000=1400, wealth below 1.5M
    [InlineData(1401, 1000, 1_500_000, false)] // Past 1400 threshold, wealth meets 1.5M
    public void CheckIfGameIsOver_AccountsForGameOffset_WhenCheckingThresholds(
        long updatesCounter,
        long gameOffset,
        decimal accountBalance,
        bool expectedGameOver)
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var account = new FlipperAccount(accountBalance, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: updatesCounter,
            gameOffset: gameOffset,
            flipperAccount: account);

        // Act
        var (isGameOver, reason) = simulation.CheckIfGameIsOver();

        // Assert
        Assert.Equal(expectedGameOver, isGameOver);
        if (expectedGameOver)
        {
            Assert.Equal("GameOverNotGoodEnough", reason);
        }
        else
        {
            Assert.Equal(string.Empty, reason);
        }
    }

    [Fact]
    public void CheckIfGameIsOver_WhenNoConditionsMet_ReturnsNoGameOver()
    {
        // Arrange
        var config = SimulationConfig.Default;
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var account = new FlipperAccount(config.DefaultAccountBalance, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 50, // Well before any threshold
            flipperAccount: account,
            config: config);

        var initialGameOffset = simulation.GameOffset;

        // Act
        var (isGameOver, reason) = simulation.CheckIfGameIsOver();

        // Assert
        Assert.False(isGameOver);
        Assert.Equal(string.Empty, reason);
        Assert.Equal(initialGameOffset, simulation.GameOffset);
    }

    #endregion
}