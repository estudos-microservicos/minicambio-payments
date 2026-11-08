using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Application.Payments;

// Mensagens trocadas com o Orders pelo Kafka (cópias locais, sem pacote compartilhado entre repositórios, de propósito).
// O JSON é camelCase, com os enums como texto.
//
// - Tópico minicambio.payment-requests (Orders → Payments): o pedido de pagamento tem o mesmo formato do corpo do
//   POST /api/v1/payments, então é lido direto como CreatePaymentRequest e passa pelo mesmo validator e handler.
// - Tópico minicambio.payment-results (Payments → Orders): PaymentResultMessage, abaixo.

/// <summary>Resultado de um pagamento finalizado, publicado com o <c>orderId</c> como chave da mensagem.</summary>
public sealed record PaymentResultMessage(
    Guid PaymentId,
    Guid OrderId,
    PaymentStatus Status,
    string? FailureReason,
    string? ProviderReference)
{
    public static PaymentResultMessage From(Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Status,
        payment.FailureReason,
        payment.ProviderReference);
}

/// <summary>Publica os resultados dos pagamentos para o Orders (implementação com Kafka em Infrastructure).</summary>
public interface IPaymentResultPublisher
{
    /// <summary>
    /// Publica o resultado e espera a confirmação do broker. Lança <see cref="MessagePublishException"/> se o Kafka
    /// não confirmar a gravação a tempo.
    /// </summary>
    Task PublishAsync(PaymentResultMessage message, CancellationToken cancellationToken);
}

/// <summary>O Kafka não confirmou a publicação (fora do ar ou lento demais).</summary>
public sealed class MessagePublishException(string message, Exception? innerException = null)
    : Exception(message, innerException);
