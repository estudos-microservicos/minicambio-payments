using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Application.Payments;

public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    string Currency,
    decimal ForeignAmount,
    BeneficiaryResponse Beneficiary,
    PaymentStatus Status,
    string? FailureReason,
    string? ProviderReference,
    DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ServedBy)
{
    public static PaymentResponse From(Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Currency,
        payment.ForeignAmount,
        new BeneficiaryResponse(payment.BeneficiaryName, payment.BeneficiaryCountry, payment.BeneficiaryAccount),
        payment.Status,
        payment.FailureReason,
        payment.ProviderReference,
        payment.ProcessingStartedAt,
        payment.CreatedAt,
        payment.UpdatedAt,
        Environment.MachineName);
}

public sealed record BeneficiaryResponse(string Name, string Country, string AccountNumber);
