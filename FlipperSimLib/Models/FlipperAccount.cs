using System.Text.Json.Nodes;

namespace FlipperSimLib.Models;

public class FlipperAccount(decimal _accountBalance, IMarketPriceProvider _provider)
{
    private readonly object _lockObj = new();

    public IEnumerable<Loan> Loans => _loans.AsReadOnly();

    public IEnumerable<RealEstateInvestment> Investments => _investments.AsReadOnly();

    public decimal AccountBalance => _accountBalance;

    #region Serialization

    public JsonObject ToJsonObject()
    {
        return new JsonObject
        {
            ["accountBalance"] = _accountBalance,
            ["loans"] = new JsonArray(_loans.Select(l => l.ToJsonObject()).ToArray()),
            ["investments"] = new JsonArray(_investments.Select(i => i.ToJsonObject()).ToArray())
        };
    }

    public static FlipperAccount FromJsonObject(JsonObject json, IMarketPriceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(provider);

        var balance = GetRequiredValue<decimal>(json, "accountBalance");
        var account = new FlipperAccount(balance, provider);

        var loansNode = json["loans"]
            ?? throw new InvalidOperationException("Required property 'loans' is missing.");

        if (loansNode is not JsonArray loansArray)
        {
            throw new InvalidOperationException("Property 'loans' must be an array.");
        }

        foreach (var loanNode in loansArray)
        {
            if (loanNode is not JsonObject loanJson)
            {
                throw new InvalidOperationException("Each loan must be a JSON object.");
            }
            account._loans.Add(Loan.FromJsonObject(loanJson));
        }

        var investmentsNode = json["investments"]
            ?? throw new InvalidOperationException("Required property 'investments' is missing.");

        if (investmentsNode is not JsonArray investmentsArray)
        {
            throw new InvalidOperationException("Property 'investments' must be an array.");
        }

        foreach (var invNode in investmentsArray)
        {
            if (invNode is not JsonObject invJson)
            {
                throw new InvalidOperationException("Each investment must be a JSON object.");
            }
            account._investments.Add(RealEstateInvestment.FromJsonObject(invJson, provider));
        }

        return account;
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

    public void AddLoan(Loan loan)
    {
        ArgumentNullException.ThrowIfNull(loan);

        lock (_lockObj)
        {
            _loans.Add(loan);
            _accountBalance += loan.LoanAmount;
        }
    }

    public void PayOffLoan(string loanId)
    {
        var loan = _loans.FirstOrDefault(l => l.LoanId == loanId)
            ?? throw new ArgumentException($"Loan with ID = [{loanId}] not found.", nameof(loanId));

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
        ArgumentNullException.ThrowIfNull(offer);

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
        var investment = _investments.FirstOrDefault(i => i.PropertyRefId == propertyRefId)
            ?? throw new ArgumentException($"Investment with PropertyRefId = [{propertyRefId}] not found.", nameof(propertyRefId));

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

    private readonly List<Loan> _loans = [];
    private readonly List<RealEstateInvestment> _investments = [];
}
