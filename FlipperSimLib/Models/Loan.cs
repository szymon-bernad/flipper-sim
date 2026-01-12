using System.Text.Json.Nodes;

namespace FlipperSimLib.Models;

public record Loan
{
    public string LoanId { get; init; } = string.Empty;
    public decimal LoanAmount { get; set; } = 0.0m;
    public decimal InterestRate { get; set; } = 0.0m;
    public int PaymentFrequency { get; set; } = 1;

    public decimal GetPaymentAmount(long updateCounter)
    {
        if (PaymentFrequency <= 0) return 0.0m;
        if (updateCounter % PaymentFrequency != 0) return 0.0m;
        return LoanAmount * InterestRate;
    }

    #region Serialization

    public JsonObject ToJsonObject()
    {
        return new JsonObject
        {
            ["loanId"] = LoanId,
            ["loanAmount"] = LoanAmount,
            ["interestRate"] = InterestRate,
            ["paymentFrequency"] = PaymentFrequency
        };
    }

    public static Loan FromJsonObject(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return new Loan
        {
            LoanId = GetRequiredValue<string>(json, "loanId"),
            LoanAmount = GetRequiredValue<decimal>(json, "loanAmount"),
            InterestRate = GetRequiredValue<decimal>(json, "interestRate"),
            PaymentFrequency = GetRequiredValue<int>(json, "paymentFrequency")
        };
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
}
