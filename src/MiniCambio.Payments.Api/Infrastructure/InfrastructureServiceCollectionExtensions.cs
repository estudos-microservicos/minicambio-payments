using Microsoft.EntityFrameworkCore;
using MiniCambio.Payments.Api.Application;
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

        // Live não tem dependências (só "o processo responde"); ready inclui o banco.
        services.AddHealthChecks().AddDbContextCheck<PaymentsDbContext>("database", tags: [ReadyTag]);

        return services;
    }
}
