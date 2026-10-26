using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiniCambio.Payments.Api.Infrastructure.Processing;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(MiniCambio.Payments.IntegrationTests.PaymentsApiFactory))]

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>
/// Sobe a API contra um Postgres real (Testcontainers), compartilhado por todos os testes do assembly.
/// O <see cref="PaymentProcessingWorker"/> hospedado é removido para que nenhum pagamento mude de status "sozinho":
/// os testes de processamento chamam o <see cref="PaymentProcessingCycle"/> diretamente.
/// </summary>
public sealed class PaymentsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await database.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }

    /// <summary>
    /// Cria uma API apontando para um banco novo e exclusivo (no mesmo container; as migrations o criam ao subir) e
    /// com o relógio controlado pelo teste. Usado pelos testes do processamento, que contam e varrem todos os
    /// pagamentos do banco e por isso não podem enxergar os dados dos outros testes.
    /// </summary>
    public WebApplicationFactory<Program> WithIsolatedDatabase(TimeProvider timeProvider)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            Database = $"payments_{Guid.NewGuid():N}",
        }.ConnectionString;

        return WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            });
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", database.GetConnectionString());
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");

        builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(descriptor =>
                !descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(PaymentProcessingWorker));
            services.Remove(worker);
        });
    }
}
