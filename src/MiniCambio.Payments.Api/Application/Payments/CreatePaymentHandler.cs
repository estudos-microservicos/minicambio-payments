using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Application.Payments;

/// <summary>Resultado da criação: <see cref="Created"/> é <c>false</c> quando a ordem já tinha pagamento (replay idempotente).</summary>
public sealed record CreatePaymentResult(PaymentResponse Payment, bool Created);

public sealed class CreatePaymentHandler(
    IPaymentRepository payments,
    TimeProvider timeProvider,
    ILogger<CreatePaymentHandler> logger)
{
    /// <summary>
    /// Registra o pagamento da ordem em <c>Pending</c> (o worker processa depois). Idempotente por <c>orderId</c>:
    /// se a ordem já tem pagamento, devolve o existente sem criar outro. Espera um request já validado.
    /// </summary>
    public async Task<CreatePaymentResult> HandleAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var orderId = request.OrderId!.Value;

        var existing = await payments.GetByOrderIdAsync(orderId, cancellationToken);
        if (existing is not null)
        {
            return new CreatePaymentResult(PaymentResponse.From(existing), Created: false);
        }

        var beneficiary = request.Beneficiary!;
        var payment = Payment.Create(
            orderId,
            request.Currency!,
            request.ForeignAmount!.Value,
            beneficiary.Name!.Trim(),
            beneficiary.Country!,
            beneficiary.AccountNumber!.Trim(),
            timeProvider.GetUtcNow());

        if (await payments.TryAddAsync(payment, cancellationToken))
        {
            logger.LogInformation("Pagamento {PaymentId} criado para a ordem {OrderId}", payment.Id, orderId);
            return new CreatePaymentResult(PaymentResponse.From(payment), Created: true);
        }

        // Corrida: outra requisição para a mesma ordem gravou entre a consulta acima e o insert. O índice único em
        // order_id barrou a duplicata, então devolvemos o pagamento que "venceu".
        var winner = await payments.GetByOrderIdAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"O índice único recusou o pagamento da ordem {orderId}, mas o pagamento existente não foi encontrado.");

        logger.LogInformation("Pagamento da ordem {OrderId} já havia sido criado por uma requisição concorrente", orderId);
        return new CreatePaymentResult(PaymentResponse.From(winner), Created: false);
    }
}
