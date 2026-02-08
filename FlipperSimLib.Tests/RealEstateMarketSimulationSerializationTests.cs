using System.Text.Json.Nodes;
using FlipperSimLib.Tests.Builders;
using FlipperSimLib.Tests.TestDoubles;

namespace FlipperSimLib.Tests;

public sealed class RealEstateMarketSimulationSerializationTests
{
    #region ToJsonObject Tests

    [Fact]
    public void ToJsonObject_FreshSimulation_ContainsAllRequiredProperties()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();
        var config = SimulationConfig.Default;

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            config: config);

        // Act
        var json = simulation.ToJsonObject();

        // Assert
        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.Equal(0L, json["updatesCounter"]!.
	GetValue<long>());
        Assert.Equal(0L, json["gameOffset"]!.GetValue<long>());
        Assert.IsType<JsonObject>(json["account"]);
        Assert.IsType<JsonArray>(json["offers"]);
        Assert.Empty(json["offers"]!.AsArray());
        Assert.False(string.IsNullOrWhiteSpace(json["savedAt"]!.GetValue<string>()));
    }

    [Fact]
    public void ToJsonObject_WithOffersAndState_SerializesAllData()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => 1_500m,
        };
        var random = new FakeRandomProvider();

        var offers = new List<RealEstateOffer>
        {
            new()
            {
                PropertyRefId = "offer1",
                Address = "123 Main St",
                CreatedAt = 5,
                UsableAreaSqMeters = 40m,
                OfferPricePerSqMeter = 1_200m,
                IsPremium = true,
            },
        };

        var account = new FlipperAccount(250_000m, priceGen);

        var simulation = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 42,
            gameOffset: 10,
            initialOffers: offers,
            flipperAccount: account);

        // Act
        var json = simulation.ToJsonObject();

        // Assert
        Assert.Equal(42L, json["updatesCounter"]!.GetValue<long>());
        Assert.Equal(10L, json["gameOffset"]!.GetValue<long>());

        var offersArray = json["offers"]!.AsArray();
        Assert.Single(offersArray);

        var offerJson = offersArray[0]!.AsObject();
        Assert.Equal("offer1", offerJson["propertyRefId"]!.GetValue<string>());
        Assert.Equal("123 Main St", offerJson["address"]!.GetValue<string>());
        Assert.Equal(40m, offerJson["usableAreaSqMeters"]!.GetValue<decimal>());
        Assert.Equal(1_200m, offerJson["offerPricePerSqMeter"]!.
	GetValue<decimal>());
        Assert.True(offerJson["isPremium"]!.GetValue<bool>());
        Assert.Equal(5L, offerJson["createdAt"]!.GetValue<long>());
    }

    [Fact]
    public void ToJsonObject_SavedAtTimestamp_IsValidIso8601()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);

        // Act
        var json = simulation.ToJsonObject();

        // Assert
        var savedAt = json["savedAt"]!.GetValue<string>();
        Assert.True(
            DateTimeOffset.TryParseExact(savedAt, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out _),
            $"savedAt value '{savedAt}' is not a valid ISO 8601 timestamp.");
    }

    #endregion

    #region FromJsonObject Roundtrip Tests

    [Fact]
    public void FromJsonObject_FreshSimulation_RoundtripsCorrectly()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();
        var config = SimulationConfig.Default;

        var original = new RealEstateMarketSimulation(
            priceGen,
            random,
            config: config);

        var json = original.ToJsonObject();

        // Act
        var restored = RealEstateMarketSimulation.FromJsonObject(json, priceGen, random, config);

        // Assert
        Assert.Equal(original.UpdatesCounter, restored.UpdatesCounter);
        Assert.Equal(original.GameOffset, restored.GameOffset);
        Assert.Equal(original.FlipperAccount.AccountBalance, restored.FlipperAccount.AccountBalance);
        Assert.Empty(restored.FlipperAccount.Investments);
        Assert.Empty(restored.FlipperAccount.Loans);
    }

    [Fact]
    public void FromJsonObject_PopulatedSimulation_RoundtripsAllState()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => 2_000m,
        };
        var random = new FakeRandomProvider();
        var config = SimulationConfig.Default;

        var account = new FlipperAccount(500_000m, priceGen);

        var investmentOffer = new RealEstateOffer
        {
            PropertyRefId = "inv1",
            Address = "456 Oak Ave",
            UsableAreaSqMeters = 50m,
            OfferPricePerSqMeter = 1_800m,
        };
        account.AddInvestment(investmentOffer);

        var loan = new LoanBuilder()
            .WithLoanId("loan1")
            .WithAmount(25_000m)
            .WithInterestRate(0.02m)
            .WithPaymentFrequency(5)
            .Build();
        account.AddLoan(loan);

        var marketOffers = new List<RealEstateOffer>
        {
            new()
            {
                PropertyRefId = "offer-a",
                Address = "789 Pine Rd",
                CreatedAt = 10,
                UsableAreaSqMeters = 35m,
                OfferPricePerSqMeter = 1_100m,
                IsPremium = false,
            },
            new()
            {
                PropertyRefId = "offer-b",
                Address = "321 Elm St",
                CreatedAt = 15,
                UsableAreaSqMeters = 80m,
                OfferPricePerSqMeter = 2_500m,
                IsPremium = true,
            },
        };

        var original = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 100,
            gameOffset: 50,
            initialOffers: marketOffers,
            flipperAccount: account,
            config: config);

        var json = original.ToJsonObject();

        // Act
        var restored = RealEstateMarketSimulation.FromJsonObject(json, priceGen, random, config);

        // Assert
        Assert.Equal(100L, restored.UpdatesCounter);
        Assert.Equal(50L, restored.GameOffset);
        Assert.Equal(account.AccountBalance, restored.FlipperAccount.AccountBalance);

        var restoredInvestments = restored.FlipperAccount.Investments.ToList();
        Assert.Single(restoredInvestments);
        Assert.Equal("inv1", restoredInvestments[0].PropertyRefId);
        Assert.Equal(investmentOffer.UsableAreaSqMeters, restoredInvestments[0].UsableAreaSqMeters);

        var restoredLoans = restored.FlipperAccount.Loans.ToList();
        Assert.Single(restoredLoans);
        Assert.Equal("loan1", restoredLoans[0].LoanId);
        Assert.Equal(25_000m, restoredLoans[0].LoanAmount);
        Assert.Equal(0.02m, restoredLoans[0].InterestRate);
        Assert.Equal(5, restoredLoans[0].PaymentFrequency);
    }

    #endregion

    #region ToJsonString / FromJsonString Tests

    [Fact]
    public void ToJsonString_ProducesValidJson_ThatCanBeDeserialized()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator
        {
            PriceResolver = (_, _) => 1_500m,
        };
        var random = new FakeRandomProvider();

        var offers = new List<RealEstateOffer>
        {
            new()
            {
                PropertyRefId = "str-offer",
                Address = "String Test Rd",
                CreatedAt = 3,
                UsableAreaSqMeters = 55m,
                OfferPricePerSqMeter = 1_400m,
            },
        };

        var original = new RealEstateMarketSimulation(
            priceGen,
            random,
            updatesCounter: 25,
            gameOffset: 5,
            initialOffers: offers);

        // Act
        var jsonString = original.ToJsonString();
        var restored = RealEstateMarketSimulation.FromJsonString(jsonString, priceGen, random);

        // Assert
        Assert.Equal(original.UpdatesCounter, restored.UpdatesCounter);
        Assert.Equal(original.GameOffset, restored.GameOffset);
        Assert.Equal(original.FlipperAccount.AccountBalance, restored.FlipperAccount.AccountBalance);
    }

    [Fact]
    public void ToJsonString_WithIndented_ProducesFormattedOutput()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);

        // Act
        var compact = simulation.ToJsonString(indented: false);
        var indented = simulation.ToJsonString(indented: true);

        // Assert
        Assert.DoesNotContain("\n", compact);
        Assert.Contains("\n", indented);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void FromJsonObject_NullJson_ThrowsArgumentNullException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            RealEstateMarketSimulation.FromJsonObject(null!, priceGen, random));
    }

    [Fact]
    public void FromJsonObject_NullMarketPriceGen_ThrowsArgumentNullException()
    {
        // Arrange
        var random = new FakeRandomProvider();
        var json = new JsonObject { ["updatesCounter"] = 0L, ["gameOffset"] = 0L };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, null!, random));
    }

    [Fact]
    public void FromJsonObject_NullRandomProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var json = new JsonObject { ["updatesCounter"] = 0L, ["gameOffset"] = 0L };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, null!));
    }

    [Theory]
    [InlineData("updatesCounter")]
    [InlineData("gameOffset")]
    [InlineData("account")]
    [InlineData("offers")]
    public void FromJsonObject_MissingRequiredProperty_ThrowsInvalidOperationException(string missingProperty)
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json.Remove(missingProperty);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));

        Assert.Contains(missingProperty, ex.Message);
    }

    [Fact]
    public void FromJsonObject_InvalidPropertyType_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json["updatesCounter"] = "not-a-number";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));

        Assert.Contains("updatesCounter", ex.Message);
    }

    [Fact]
    public void FromJsonObject_AccountNotJsonObject_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json["account"] = "not-an-object";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));

        Assert.Contains("account", ex.Message);
    }

    [Fact]
    public void FromJsonObject_OffersNotArray_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json["offers"] = "not-an-array";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));

        Assert.Contains("offers", ex.Message);
    }

    [Fact]
    public void FromJsonObject_OfferNotJsonObject_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json["offers"] = new JsonArray(JsonValue.Create("not-an-object"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));
    }

    [Fact]
    public void FromJsonObject_UnsupportedVersion_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json["version"] = 999;

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonObject(json, priceGen, random));

        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void FromJsonObject_WithoutVersion_DeserializesSuccessfully()
    {
        // Arrange — simulates JSON saved before version field was added
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        var simulation = new RealEstateMarketSimulation(priceGen, random);
        var json = simulation.ToJsonObject();
        json.Remove("version");

        // Act
        var restored = RealEstateMarketSimulation.FromJsonObject(json, priceGen, random);

        // Assert
        Assert.Equal(simulation.UpdatesCounter, restored.UpdatesCounter);
        Assert.Equal(simulation.GameOffset, restored.GameOffset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJsonString_NullOrWhitespace_ThrowsArgumentException(string? input)
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
            RealEstateMarketSimulation.FromJsonString(input!, priceGen, random));
    }

    [Fact]
    public void FromJsonString_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonString("{{{invalid", priceGen, random));
    }

    [Fact]
    public void FromJsonString_JsonArray_ThrowsInvalidOperationException()
    {
        // Arrange
        var priceGen = new FakeMarketPriceGenerator();
        var random = new FakeRandomProvider();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            RealEstateMarketSimulation.FromJsonString("[1, 2, 3]", priceGen, random));
    }

    #endregion
}