using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using MiniCambio.Payments.Api.Application.Payments;

namespace MiniCambio.Payments.Api.Infrastructure.Messaging;

/// <summary>
/// Publica os resultados dos pagamentos no Kafka. É singleton: o producer do Kafka mantém conexões abertas com o
/// broker e foi feito para ser compartilhado pela aplicação inteira.
/// </summary>
public sealed class KafkaPaymentResultPublisher : IPaymentResultPublisher, IDisposable
{
    public const string UnavailableMessage = "O Kafka não confirmou a publicação do resultado do pagamento.";

    private readonly IProducer<string, string> producer;
    private readonly string topic;
    private readonly ILogger<KafkaPaymentResultPublisher> logger;

    public KafkaPaymentResultPublisher(IOptions<KafkaOptions> options, ILogger<KafkaPaymentResultPublisher> logger)
    {
        this.logger = logger;
        topic = options.Value.PaymentResultsTopic;

        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = Environment.MachineName, // aparece nos logs do broker: dá para saber qual Pod publicou

            // Só considera publicado quando o broker gravou a mensagem; o idempotence evita duplicatas nas novas
            // tentativas internas do próprio cliente.
            Acks = Acks.All,
            EnableIdempotence = true,

            // Com o Kafka fora, o padrão (5 min) seguraria o lote travado no banco. Em 5 s a publicação desiste e o
            // worker tenta de novo no próximo ciclo (o resultado continua marcado como "não publicado").
            MessageTimeoutMs = 5000,
        };

        producer = new ProducerBuilder<string, string>(config)
            // Sem isto, o librdkafka escreve os avisos dele direto no console, fora do JSON dos logs.
            .SetLogHandler((_, message) => logger.Log(
                KafkaMessages.ToLogLevel(message.Level), "Kafka ({Facility}): {Message}", message.Facility, message.Message))
            .SetErrorHandler((_, error) => logger.LogWarning("Kafka (producer): {Reason}", error.Reason))
            .Build();
    }

    public async Task PublishAsync(PaymentResultMessage message, CancellationToken cancellationToken)
    {
        var kafkaMessage = new Message<string, string>
        {
            // Mesma chave → mesma partição: as mensagens de uma ordem ficam sempre na mesma fila, em ordem.
            Key = message.OrderId.ToString(),
            Value = JsonSerializer.Serialize(message, KafkaMessages.JsonOptions),
        };

        try
        {
            var result = await producer.ProduceAsync(topic, kafkaMessage, cancellationToken);
            logger.LogInformation(
                "Resultado do pagamento {PaymentId} da ordem {OrderId} gravado em {Topic} (partição {Partition}, offset {Offset})",
                message.PaymentId,
                message.OrderId,
                result.Topic,
                result.Partition.Value,
                result.Offset.Value);
        }
        catch (KafkaException ex)
        {
            throw new MessagePublishException(UnavailableMessage, ex);
        }
    }

    public void Dispose()
    {
        // Entrega o que ainda estiver no buffer antes de encerrar (o Pod recebe SIGTERM e tem alguns segundos).
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }
}
