using FlipperSimLib.Models;

namespace FlipperSimLib
{
    public class RealEstateMarketSimulation(IMarketPriceGenerator _marketPriceGen, int? _randomSeed)
    {
        public FlipperAccount FlipperAccount => _flipperAccount;

        public bool IsFreshGame => _gameOffset >= _updatesCounter;

        public IEnumerable<RealEstateOffer> GetUpdatedRealEstateOffers(int count)
        {
            ++_updatesCounter;

            _marketPriceGen.RunGenerator();

            var loanPayments = 0.0m;
            foreach (var loan in FlipperAccount.Loans)
            {
                loanPayments += loan.GetPaymentAmount(_updatesCounter);
            }
            FlipperAccount.HandlePayment(loanPayments);

            foreach (var inv in FlipperAccount.Investments)
            {
                inv.CheckUpgradeProgress(_updatesCounter);
            }

            var toBeDeleted = new List<string>();
            foreach (var offer in _realEstateOffers)
            {
                if (offer.CreatedAt < _updatesCounter - 30)
                {
                    toBeDeleted.Add(offer.PropertyRefId);
                }
            }

            foreach(var offId in toBeDeleted)
            {
                DeleteOffer(offId);
            }

            // Update all existing offer
            for (int i = 0; i < _realEstateOffers.Count; i++)
            {
                _realEstateOffers[i] = _realEstateOffers[i] with
                {
                    OfferPricePerSqMeter = Math.Round(
                        (0.6m * _realEstateOffers[i].OfferPricePerSqMeter + 0.4m * 
                            GetMarketPricePerSqMeter(_realEstateOffers[i].UsableAreaSqMeters, _realEstateOffers[i].IsPremium)), 2),

                };
            }

            int offersToGenerate = count - _realEstateOffers.Count;
            // Only generate new offers if we need more than what we currently have
            if (offersToGenerate > 0)
            {
                for (int i = 0; i < offersToGenerate; i++)
                {
                    var area = 18.0m + (decimal)_rndInstance.NextDouble() * 101.0m;
                    var isPremium = (_rndInstance.NextDouble() > 0.75);
                    _realEstateOffers.Add(new RealEstateOffer
                    {
                        CreatedAt = _updatesCounter,
                        PropertyRefId = Guid.NewGuid().ToString("N"),
                        Address = "** REDACTED **",
                        UsableAreaSqMeters = Math.Round(area, 2),
                        OfferPricePerSqMeter = Math.Round((1.0m + ((decimal)(_rndInstance.NextDouble() - 0.5)*0.123m)) * GetMarketPricePerSqMeter(area, isPremium), 0),
                        IsPremium = isPremium,
                    });
                }
            }

            return _realEstateOffers.Take(count);
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
            var upgradeFee = 0.125m * investment.GetCurrentPrice();
            if (!investment.IsPremium && FlipperAccount.AccountBalance > upgradeFee)
            {
                investment.Upgrade(upgradeFee, _updatesCounter);
                FlipperAccount.HandlePayment(upgradeFee);
                return true;
            }

            return false;
        }

        public (bool, string) CheckIfGameIsOver()
        {
            ICollection<(long, int)> gameThresholds = [(200, 750_000), (400, 1_500_000), (600, 3_000_000), (800, 5_000_000)];
            var wealthSum = FlipperAccount.Investments.Sum(i => i.GetCurrentPrice()) + FlipperAccount.AccountBalance;
            foreach (var gt in gameThresholds)
            {
                if (_updatesCounter > gt.Item1 + _gameOffset && wealthSum < gt.Item2)
                {
                    _gameOffset = _updatesCounter;
                    return (true, "You are not good enough FLIPPER to continue. GAME OVER");
                }
            }

            if (FlipperAccount.AccountBalance < 0)
            {
                _gameOffset = _updatesCounter ;
                return (true, "Your account balance is negative. GAME OVER");
            }

            return (false, string.Empty);
        }

        public void ResetAccount()
        {
            _flipperAccount = new FlipperAccount(100_000m, _marketPriceGen);
        }

        private readonly Random _rndInstance = new Random(_randomSeed ?? (int)DateTime.Now.Ticks);

        private long _updatesCounter = 0;

        private long _gameOffset = 0;

        private readonly IList<RealEstateOffer> _realEstateOffers = new List<RealEstateOffer>();

        private FlipperAccount _flipperAccount = new FlipperAccount(100_000m, _marketPriceGen);
    }
}
