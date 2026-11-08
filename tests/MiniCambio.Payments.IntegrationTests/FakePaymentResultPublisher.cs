using System.Collections.Concurrent;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Infrastructure.Messaging;

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>
/// Publicador falso no lugar do Kafka: guarda cada resultado publicado e pode ser "desligado" com
/// <see cref="Unavailable"/>, como o Kafka fora do ar.
/// </summary>
public sealed class FakePaymentResultPublisher : IPaymentResultPublisher
{
    public bool Unavailable { get; set; }

    public ConcurrentQueue<PaymentResultMessage> Messages { get; } = new();

    public Task PublishAsync(PaymentResultMessage message, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new MessagePublishException(KafkaPaymentResultPublisher.UnavailableMessage);
        }

        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
