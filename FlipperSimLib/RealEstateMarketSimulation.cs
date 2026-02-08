using System.Text.Json;
using System.Text.Json.Nodes;
using FlipperSimLib.Models;

namespace FlipperSimLib
{
    public class RealEstateMarketSimulation
    {
        private const int CurrentSerializationVersion = 1;

        private readonly IMarketPriceGenerator _marketPriceGen;
        private readonly IRandomProvider _randomProvider;
        private readonly IList<RealEstateOffer> _realEstateOffers;
        private readonly SimulationConfig _config;
        private FlipperAccount _flipperAccount;
        private long _updatesCounter;
        private long _gameOffset;

        public RealEstateMarketSimulation(IMarketPriceGenerator marketPriceGen, int? randomSeed)
            : this(marketPriceGen, new SystemRandomProvider(randomSeed), config: SimulationConfig.Default)
        {
        }

        public RealEstateMarketSimulation(
            IMarketPriceGenerator marketPriceGen,
            IRandomProvider randomProvider,
            long updatesCounter = 0,
            long gameOffset = 0,
            IList<RealEstateOffer>? initialOffers = null,
            FlipperAccount? flipperAccount = null,
            SimulationConfig? config = null)
        {
            _marketPriceGen = marketPriceGen;
            _randomProvider = randomProvider;
            _updatesCounter = updatesCounter;
            _gameOffset = gameOffset;
            _realEstateOffers = initialOffers ?? new List<RealEstateOffer>();
            _config = config ?? SimulationConfig.Default;
            _flipperAccount = flipperAccount ?? new FlipperAccount(_config.DefaultAccountBalance, _marketPriceGen);
        }

        public FlipperAccount FlipperAccount => _flipperAccount;

        public bool IsFreshGame => _gameOffset >= _updatesCounter;

        public long UpdatesCounter => _updatesCounter;

        public long GameOffset => _gameOffset;

        public SimulationConfig Config => _config;

        #region Serialization

        public JsonObject ToJsonObject()
        {
            return new JsonObject
            {
                ["version"] = CurrentSerializationVersion,
                ["updatesCounter"] = _updatesCounter,
                ["gameOffset"] = _gameOffset,
                ["account"] = _flipperAccount.ToJsonObject(),
                ["offers"] = new JsonArray(_realEstateOffers.Select(o => o.ToJsonObject()).ToArray()),
                ["savedAt"] = DateTime.UtcNow.ToString("O")
            };
        }

        public string ToJsonString(bool indented = false)
        {
            var jsonObject = ToJsonObject();
            var options = new JsonSerializerOptions { WriteIndented = indented };
            return jsonObject.ToJsonString(options);
        }

        public static RealEstateMarketSimulation FromJsonString(
            string json,
            IMarketPriceGenerator marketPriceGen,
            IRandomProvider randomProvider,
            SimulationConfig? config = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            ArgumentNullException.ThrowIfNull(marketPriceGen);
            ArgumentNullException.ThrowIfNull(randomProvider);

            JsonObject jsonObject;
            try
            {
                var node = JsonNode.Parse(json)
                    ?? throw new InvalidOperationException("Parsed JSON is null.");

                jsonObject = node as JsonObject
                    ?? throw new InvalidOperationException("JSON root must be an object.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Invalid JSON format.", ex);
            }

            return FromJsonObject(jsonObject, marketPriceGen, randomProvider, config);
        }

        public static RealEstateMarketSimulation FromJsonObject(
            JsonObject json,
            IMarketPriceGenerator marketPriceGen,
            IRandomProvider randomProvider,
            SimulationConfig? config = null)
        {
            ArgumentNullException.ThrowIfNull(json);
            ArgumentNullException.ThrowIfNull(marketPriceGen);
            ArgumentNullException.ThrowIfNull(randomProvider);

            if (json.ContainsKey("version"))
            {
                var version = GetRequiredValue<int>(json, "version");
                if (version > CurrentSerializationVersion)
                {
                    throw new InvalidOperationException(
                        $"Unsupported serialization version {version}. Maximum supported version is {CurrentSerializationVersion}.");
                }
            }

            var updatesCounter = GetRequiredValue<long>(json, "updatesCounter");
            var gameOffset = GetRequiredValue<long>(json, "gameOffset");

            var accountNode = json["account"]
                ?? throw new InvalidOperationException("Required property 'account' is missing.");

            if (accountNode is not JsonObject accountJson)
            {
                throw new InvalidOperationException("Property 'account' must be a JSON object.");
            }

            var account = FlipperAccount.FromJsonObject(accountJson, marketPriceGen);

            var offersNode = json["offers"]
                ?? throw new InvalidOperationException("Required property 'offers' is missing.");

            if (offersNode is not JsonArray offersArray)
            {
                throw new InvalidOperationException("Property 'offers' must be an array.");
            }

            var offers = new List<RealEstateOffer>();
            foreach (var offerNode in offersArray)
            {
                if (offerNode is not JsonObject offerJson)
                {
                    throw new InvalidOperationException("Each offer must be a JSON object.");
                }
                offers.Add(RealEstateOffer.FromJsonObject(offerJson));
            }

            return new RealEstateMarketSimulation(
                marketPriceGen,
                randomProvider,
                updatesCounter,
                gameOffset,
                offers,
                account,
                config);
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

        public IEnumerable<RealEstateOffer> GetRealEstateOffersAfterUpdates(int count)
        {
            ++_updatesCounter;
            _marketPriceGen.RunGenerator();

            var lumpSum = CalculateNetCashFlow(_updatesCounter);
            FlipperAccount.HandlePayment(lumpSum);

            RemoveExpiredOffers(_updatesCounter);
            RefreshExistingOffers();
            GenerateOffersIfNeeded(count);

            return _realEstateOffers.Take(count);
        }

        private decimal CalculateNetCashFlow(long updateCounter)
        {
            var lumpSum = 0.0m;

            foreach (var loan in FlipperAccount.Loans)
            {
                lumpSum -= loan.GetPaymentAmount(updateCounter);
            }

            foreach (var investment in FlipperAccount.Investments)
            {
                investment.CheckUpgradeProgress(updateCounter);
                if (investment.IsBeingRented)
                {
                    lumpSum += investment.GetPaymentAmount(updateCounter);
                }
            }

            return lumpSum;
        }

        private void RemoveExpiredOffers(long currentUpdate)
        {
            var expiredOffers = _realEstateOffers
                .Where(o => o.CreatedAt < currentUpdate - _config.OfferExpirationDays)
                .Select(o => o.PropertyRefId)
                .ToArray();

            foreach (var propertyRefId in expiredOffers)
            {
                DeleteOffer(propertyRefId);
            }
        }

        private void RefreshExistingOffers()
        {
            for (var i = 0; i < _realEstateOffers.Count; i++)
            {
                var offer = _realEstateOffers[i];
                var recalculatedPrice = Math.Round(
                    (_config.OfferPriceRetentionWeight * offer.OfferPricePerSqMeter) +
                    (_config.MarketPriceInfluenceWeight * GetMarketPricePerSqMeter(offer.UsableAreaSqMeters, offer.IsPremium)),
                    2);

                _realEstateOffers[i] = offer with { OfferPricePerSqMeter = recalculatedPrice };
            }
        }

        private void GenerateOffersIfNeeded(int requestedCount)
        {
            var offersToGenerate = requestedCount - _realEstateOffers.Count;
            if (offersToGenerate <= 0)
            {
                return;
            }

            for (var i = 0; i < offersToGenerate; i++)
            {
                _realEstateOffers.Add(CreateOffer());
            }
        }

        private RealEstateOffer CreateOffer()
        {
            var area = _config.MinPropertyArea + (decimal)_randomProvider.NextDouble() * _config.PropertyAreaRange;
            var isPremium = _randomProvider.NextDouble() > _config.PremiumPropertyThreshold;

            return new RealEstateOffer
            {
                CreatedAt = _updatesCounter,
                PropertyRefId = Guid.NewGuid().ToString("N"),
                Address = "** REDACTED **",
                UsableAreaSqMeters = Math.Round(area, 2),
                OfferPricePerSqMeter = Math.Round(
                    (1.0m + ((decimal)(_randomProvider.NextDouble() - 0.5) * _config.PriceVariationFactor)) *
                    GetMarketPricePerSqMeter(area, isPremium), 0),
                IsPremium = isPremium,
            };
        }

        public decimal GetMarketPricePerSqMeter(decimal area, bool isPremium) => _marketPriceGen.GetMarketPricePerSqMeter(area, isPremium);

        public decimal GetInterestRate() => _marketPriceGen.GetInterestRate();

        public void DeleteOffer(string propertyRefId)
        {
            var offer = _realEstateOffers.FirstOrDefault(o => o.PropertyRefId == propertyRefId);
            if (offer is not null)
            {
                _realEstateOffers.Remove(offer);
            }
        }

        public void BuyOffer(string propertyRefId)
        {
            var offer = _realEstateOffers.FirstOrDefault(o => o.PropertyRefId == propertyRefId)
                ?? throw new ArgumentException($"Offer with PropertyRefId = [{propertyRefId}] not found.", nameof(propertyRefId));
            DeleteOffer(propertyRefId);
            FlipperAccount.AddInvestment(offer);
        }

        public void SellInvestment(string propertyRefId)
        {
            FlipperAccount.SellInvestment(propertyRefId);
        }

        public bool UpgradeInvestment(string propertyRefId)
        {
            var investment = FlipperAccount.Investments.First(i => i.PropertyRefId == propertyRefId);
            var upgradeFee = Math.Round(_config.UpgradeFeeRate * investment.GetCurrentPrice(), 2, MidpointRounding.AwayFromZero);
            if (!investment.IsPremium && FlipperAccount.AccountBalance > upgradeFee)
            {
                investment.Upgrade(upgradeFee, _updatesCounter);
                FlipperAccount.HandlePayment(-upgradeFee);
                return true;
            }

            return false;
        }

        public bool ToggleInvestmentRenting(string propertyRefId)
        {
            var investment = FlipperAccount.Investments.FirstOrDefault(i => i.PropertyRefId == propertyRefId);
            if (investment is not null)
            {
                if (investment.IsBeingRented)
                {
                    investment.EndRenting();
                }
                else
                {
                    investment.RentProperty();
                }
                return true;
            }

            return false;
        }

        public (bool, string) CheckIfGameIsOver()
        {
            var wealthSum = FlipperAccount.Investments.Sum(i => i.GetCurrentPrice()) + FlipperAccount.AccountBalance;
            foreach (var gt in _config.GameThresholds)
            {
                if (_updatesCounter > gt.UpdateThreshold + _gameOffset && wealthSum < gt.WealthRequirement)
                {
                    _gameOffset = _updatesCounter;
                    return (true, "GameOverNotGoodEnough");
                }
            }

            if (FlipperAccount.AccountBalance < 0)
            {
                _gameOffset = _updatesCounter;
                return (true, "GameOverNegativeBalance");
            }

            return (false, string.Empty);
        }

        public void ResetAccount()
        {
            _flipperAccount = new FlipperAccount(_config.DefaultAccountBalance, _marketPriceGen);
        }
    }

    public record GameThreshold(long UpdateThreshold, decimal WealthRequirement);

    public record SimulationConfig
    {
        /// <summary>
        /// Default starting balance for a new account.
        /// </summary>
        public decimal DefaultAccountBalance { get; init; } = 100_000m;

        /// <summary>
        /// Number of updates after which an offer expires.
        /// </summary>
        public int OfferExpirationDays { get; init; } = 30;

        /// <summary>
        /// Weight of existing offer price when recalculating (0.0 - 1.0).
        /// </summary>
        public decimal OfferPriceRetentionWeight { get; init; } = 0.6m;

        /// <summary>
        /// Weight of market price when recalculating offer price (0.0 - 1.0).
        /// </summary>
        public decimal MarketPriceInfluenceWeight { get; init; } = 0.4m;

        /// <summary>
        /// Minimum property area in square meters.
        /// </summary>
        public decimal MinPropertyArea { get; init; } = 18.0m;

        /// <summary>
        /// Range of property area (added to MinPropertyArea).
        /// </summary>
        public decimal PropertyAreaRange { get; init; } = 101.0m;

        /// <summary>
        /// Random threshold above which a property is considered premium (0.0 - 1.0).
        /// </summary>
        public double PremiumPropertyThreshold { get; init; } = 0.75;

        /// <summary>
        /// Factor for price variation when creating offers.
        /// </summary>
        public decimal PriceVariationFactor { get; init; } = 0.123m;

        /// <summary>
        /// Rate applied to current price for upgrade fee calculation.
        /// </summary>
        public decimal UpgradeFeeRate { get; init; } = 0.1555m;

        /// <summary>
        /// Game thresholds defining update milestones and required wealth.
        /// </summary>
        public IReadOnlyList<GameThreshold> GameThresholds { get; init; } =
        [
            new(200, 750_000m),
            new(400, 1_500_000m),
            new(600, 3_000_000m),
            new(800, 5_000_000m)
        ];

        /// <summary>
        /// Default configuration with standard game values.
        /// </summary>
        public static SimulationConfig Default { get; } = new();
    }
}
