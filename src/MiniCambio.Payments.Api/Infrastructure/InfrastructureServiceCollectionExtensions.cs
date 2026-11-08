using Microsoft.EntityFrameworkCore;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Infrastructure.Messaging;
using MiniCambio.Payments.Api.Infrastructure.Persistence;
using MiniCambio.Payments.Api.Infrastructure.Processing;

namespace MiniCambio.Payments.Api.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<PaymentsDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'Default' não configurada (variável ConnectionStrings__Default).");
            }

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // Processamento assíncrono mockado: o worker chama o ciclo em loop, um escopo por ciclo.
        services.AddScoped<PaymentProcessingCycle>();
        services.AddHostedService<PaymentProcessingWorker>();

        // Kafka: o consumidor dos pedidos de pagamento (roda em cada réplica, no mesmo grupo de consumo) e o
        // publicador dos resultados (singleton, um producer por Pod), usado pelo ciclo de processamento.
        services.AddOptions<KafkaOptions>()
            .BindConfiguration(KafkaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<KafkaPaymentResultPublisher>();
        services.AddSingleton<IPaymentResultPublisher>(provider => provider.GetRequiredService<KafkaPaymentResultPublisher>());
        services.AddHostedService<PaymentRequestsConsumer>();

        // Live não tem dependências (só "o processo responde"); ready inclui o banco. O Kafka fica de fora de
        // propósito: com ele fora, a API continua atendendo consultas e os pedidos esperam no tópico.
        services.AddHealthChecks().AddDbContextCheck<PaymentsDbContext>("database", tags: [ReadyTag]);

        return services;
    }
}
