namespace FlipperSimLib.Tests.Builders;

public sealed class LoanBuilder
{
    private string _loanId = Guid.NewGuid().ToString("N");
    private decimal _loanAmount = 50_000m;
    private decimal _interestRate = 0.01m;
    private int _paymentFrequency = 10;

    public LoanBuilder WithLoanId(string value)
    {
        _loanId = value;
        return this;
    }

    public LoanBuilder WithAmount(decimal value)
    {
        _loanAmount = value;
        return this;
    }

    public LoanBuilder WithInterestRate(decimal value)
    {
        _interestRate = value;
        return this;
    }

    public LoanBuilder WithPaymentFrequency(int value)
    {
        _paymentFrequency = value;
        return this;
    }

    public Loan Build() => new()
    {
        LoanId = _loanId,
        LoanAmount = _loanAmount,
        InterestRate = _interestRate,
        PaymentFrequency = _paymentFrequency,
    };
}