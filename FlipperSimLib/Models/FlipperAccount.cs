namespace FlipperSimLib.Models;

public class FlipperAccount(decimal _accountBalance, IMarketPriceProvider _provider)
{

    private readonly object _lockObj = new object();

    public IEnumerable<Loan> Loans => _loans.AsReadOnly();

    public IEnumerable<RealEstateInvestment> Investments => _investments.AsReadOnly();

    public decimal AccountBalance => _accountBalance;

    public void AddLoan(Loan loan)
    {
        if (loan is null)
        {
            throw new ArgumentNullException(nameof(loan));
        }

        lock (_lockObj)
        {
            _loans.Add(loan);
            _accountBalance += loan.LoanAmount;
        }

    }

    public void PayOffLoan(string loanId)
    {
        var loan = _loans.FirstOrDefault(l => l.LoanId == loanId);
        if (loan is null)
        {
            throw new ArgumentException($"Loan with ID = [{loanId}] not found.", nameof(loanId));
        }

        lock (_lockObj)
        {
            if (_accountBalance < loan.LoanAmount)
            {
                throw new InvalidOperationException("Insufficient funds to pay off the loan.");
            }

            _accountBalance -= loan.LoanAmount;
            _loans.Remove(loan);
        }
    }

    public void AddInvestment(RealEstateOffer offer)
    {
        if (offer is null)
        {
            throw new ArgumentNullException(nameof(offer));
        }
        if (_accountBalance < offer.TotalPrice())
        {
            throw new InvalidOperationException("Insufficient funds to make the investment.");
        }

        var investment = new RealEstateInvestment(_provider)
        {
            PropertyRefId = offer.PropertyRefId,
            Address = offer.Address,
            PurchasePrice = offer.TotalPrice(),
            UsableAreaSqMeters = offer.UsableAreaSqMeters,
            IsPremium = offer.IsPremium,
        };

        lock (_lockObj)
        {
            _investments.Add(investment);
            _accountBalance -= offer.TotalPrice();
        }
    }

    public void SellInvestment(string propertyRefId)
    {
        var investment = _investments.FirstOrDefault(i => i.PropertyRefId == propertyRefId);
        if (investment is null)
        {
            throw new ArgumentException($"Investment with PropertyRefId = [{propertyRefId}] not found.", nameof(propertyRefId));
        }

        lock (_lockObj)
        {
            _accountBalance += investment.GetCurrentPrice();
            _investments.Remove(investment);
        }
    }

    public void HandlePayment(decimal amount)
    {
        lock (_lockObj)
        {
            _accountBalance += amount;
        }
    }

    private readonly List<Loan> _loans = new List<Loan>();

    private readonly List<RealEstateInvestment> _investments = new List<RealEstateInvestment>();
}
