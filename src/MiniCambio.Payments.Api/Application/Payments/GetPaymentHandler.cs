namespace MiniCambio.Payments.Api.Application.Payments;

public sealed class GetPaymentHandler(IPaymentRepository payments)
{
    public async Task<PaymentResponse> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await payments.GetAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Pagamento não encontrado.");

        return PaymentResponse.From(payment);
    }
}
