namespace MiniCambio.Payments.Api.Domain;

/// <summary>
/// Decide o resultado mockado de um pagamento. As regras são avaliadas nesta ordem e a primeira que casar vence:
/// <list type="number">
/// <item>nome do beneficiário contém <c>FAIL</c> (sem diferenciar maiúsculas) → <c>BENEFICIARY_REJECTED</c>;</item>
/// <item>país do beneficiário está na lista de bloqueados → <c>COMPLIANCE_BLOCKED</c>;</item>
/// <item>valor acima do limite → <c>LIMIT_EXCEEDED</c>;</item>
/// <item>caso contrário → concluído, com referência <c>MOCK-</c> + 8 caracteres alfanuméricos.</item>
/// </list>
/// Classe pura (sem I/O): a fonte de aleatoriedade é injetada para que os testes sejam determinísticos.
/// </summary>
public sealed class MockPaymentOutcomePolicy(IEnumerable<string> blockedCountries, decimal maxAmount, Random random)
{
    public const string ProviderReferencePrefix = "MOCK-";
    public const int ProviderReferenceRandomLength = 8;

    private const string FailMarker = "FAIL";
    private const string ProviderReferenceAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private readonly HashSet<string> _blockedCountries =
        new(blockedCountries.Select(country => country.Trim()), StringComparer.OrdinalIgnoreCase);

    public PaymentOutcome Decide(Payment payment)
    {
        if (payment.BeneficiaryName.Contains(FailMarker, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentOutcome.Failed(PaymentFailureReasons.BeneficiaryRejected);
        }

        if (_blockedCountries.Contains(payment.BeneficiaryCountry))
        {
            return PaymentOutcome.Failed(PaymentFailureReasons.ComplianceBlocked);
        }

        if (payment.ForeignAmount > maxAmount)
        {
            return PaymentOutcome.Failed(PaymentFailureReasons.LimitExceeded);
        }

        var suffix = random.GetItems<char>(ProviderReferenceAlphabet, ProviderReferenceRandomLength);
        return PaymentOutcome.Completed(ProviderReferencePrefix + new string(suffix));
    }
}
