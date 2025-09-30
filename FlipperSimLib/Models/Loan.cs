namespace FlipperSimLib.Models;

public record Loan
{
    public string LoanId { get; init; } = string.Empty;

    public decimal LoanAmount { get; set; } = 0.0m;

    public decimal InterestRate { get; set; } = 0.0m;

    public int PaymentFrequency { get; set; } = 1;

    public decimal GetPaymentAmount(long updateCounter)
    {
        if (PaymentFrequency <= 0)
        {
            return 0.0m;
        }

        if (updateCounter % PaymentFrequency != 0)
        {
            return 0.0m;
        }

        return LoanAmount * InterestRate;
    }
}
