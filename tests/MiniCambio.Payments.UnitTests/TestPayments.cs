using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.UnitTests;

/// <summary>Fábrica de pagamentos válidos para os testes, com os campos relevantes sobrescrevíveis.</summary>
internal static class TestPayments
{
    public static readonly DateTimeOffset Now = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    public static Payment Create(
        string beneficiaryName = "Maria Silva",
        string beneficiaryCountry = "BR",
        decimal foreignAmount = 100m,
        Guid? orderId = null,
        DateTimeOffset? now = null) =>
        Payment.Create(
            orderId ?? Guid.NewGuid(),
            "USD",
            foreignAmount,
            beneficiaryName,
            beneficiaryCountry,
            "12345-6",
            now ?? Now);

    /// <summary>Pagamento já levado ao status pedido pelas transições válidas.</summary>
    public static Payment InStatus(PaymentStatus status)
    {
        var payment = Create();
        if (status == PaymentStatus.Pending)
        {
            return payment;
        }

        payment.StartProcessing(Now);
        if (status == PaymentStatus.Completed)
        {
            payment.Complete("MOCK-ABCD1234", Now);
        }
        else if (status == PaymentStatus.Failed)
        {
            payment.Fail(PaymentFailureReasons.BeneficiaryRejected, Now);
        }

        return payment;
    }
}
