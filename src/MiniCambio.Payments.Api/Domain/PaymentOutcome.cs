namespace MiniCambio.Payments.Api.Domain;

/// <summary>Códigos de recusa devolvidos em <c>failureReason</c> (o frontend traduz cada um para o usuário).</summary>
public static class PaymentFailureReasons
{
    public const string BeneficiaryRejected = "BENEFICIARY_REJECTED";
    public const string ComplianceBlocked = "COMPLIANCE_BLOCKED";
    public const string LimitExceeded = "LIMIT_EXCEEDED";
}

/// <summary>Resultado do processamento de um pagamento: concluído com referência do provedor ou recusado com motivo.</summary>
public sealed record PaymentOutcome
{
    private PaymentOutcome(PaymentStatus status, string? providerReference, string? failureReason)
    {
        Status = status;
        ProviderReference = providerReference;
        FailureReason = failureReason;
    }

    public PaymentStatus Status { get; }

    public string? ProviderReference { get; }

    public string? FailureReason { get; }

    public static PaymentOutcome Completed(string providerReference) =>
        new(PaymentStatus.Completed, providerReference, null);

    public static PaymentOutcome Failed(string failureReason) =>
        new(PaymentStatus.Failed, null, failureReason);
}
