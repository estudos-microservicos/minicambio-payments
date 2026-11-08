using FluentValidation;

namespace MiniCambio.Payments.Api.Application.Payments;

/// <summary>O que o <see cref="PaymentRequestMessageHandler"/> fez com uma mensagem.</summary>
public enum PaymentRequestOutcome
{
    /// <summary>Pagamento criado em <c>Pending</c>.</summary>
    Created,

    /// <summary>A ordem já tinha pagamento: mensagem repetida, nada muda.</summary>
    AlreadyExists,

    /// <summary>Pedido inválido: descartado (fica só no log).</summary>
    Rejected,
}

/// <summary>
/// Trata um pedido de pagamento vindo do Kafka com as mesmas regras do <c>POST /api/v1/payments</c>: o mesmo
/// validator e o mesmo <see cref="CreatePaymentHandler"/>. A idempotência por <c>orderId</c> torna seguro receber o
/// mesmo pedido mais de uma vez (o consumidor entrega "pelo menos uma vez").
/// </summary>
public sealed class PaymentRequestMessageHandler(
    IValidator<CreatePaymentRequest> validator,
    CreatePaymentHandler createPayment,
    ILogger<PaymentRequestMessageHandler> logger)
{
    public async Task<PaymentRequestOutcome> HandleAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            // Não há a quem devolver um 400: o erro fica no log e a mensagem é descartada.
            logger.LogError(
                "Pedido de pagamento da ordem {OrderId} descartado por dados inválidos: {Errors}",
                request.OrderId,
                string.Join("; ", validation.Errors.Select(x => $"{x.PropertyName}: {x.ErrorMessage}")));
            return PaymentRequestOutcome.Rejected;
        }

        var result = await createPayment.HandleAsync(request, cancellationToken);
        if (!result.Created)
        {
            logger.LogInformation(
                "Pedido de pagamento da ordem {OrderId} repetido; o pagamento {PaymentId} já existe",
                request.OrderId,
                result.Payment.Id);
            return PaymentRequestOutcome.AlreadyExists;
        }

        return PaymentRequestOutcome.Created;
    }
}
