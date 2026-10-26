using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MiniCambio.Payments.Api.Infrastructure.Persistence;
using MiniCambio.Payments.Api.Infrastructure.Processing;

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>
/// Ciclo de processamento contra o Postgres real. Cada teste usa um banco exclusivo e um relógio falso e chama o
/// <see cref="PaymentProcessingCycle"/> diretamente (o worker hospedado é removido pela factory), então o
/// resultado é determinístico. Os pagamentos são criados e consultados pela API.
/// </summary>
public sealed partial class PaymentProcessingTests(PaymentsApiFactory factory) : IAsyncLifetime
{
    // Valor padrão de Payments__SimulatedDelaySeconds (appsettings.json).
    private static readonly TimeSpan SimulatedDelay = TimeSpan.FromSeconds(3);

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
    private WebApplicationFactory<Program> api = null!;
    private HttpClient client = null!;

    public ValueTask InitializeAsync()
    {
        api = factory.WithIsolatedDatabase(time);
        client = api.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await api.DisposeAsync();

    [Fact]
    public async Task RunOnce_PendingPayment_MarksProcessingWithStartedAt()
    {
        var id = await PaymentRequests.CreateAsync(client);
        time.Advance(TimeSpan.FromSeconds(1));

        var result = await RunCycleAsync();

        Assert.Equal(new PaymentProcessingCycleResult(Claimed: 1, Finished: 0), result);
        var payment = await PaymentRequests.GetAsync(client, id);
        Assert.Equal("Processing", payment.GetProperty("status").GetString());
        Assert.Equal(time.GetUtcNow(), payment.GetProperty("processingStartedAt").GetDateTimeOffset());
        Assert.Equal(time.GetUtcNow(), payment.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task RunOnce_BeforeSimulatedDelay_KeepsProcessing()
    {
        var id = await PaymentRequests.CreateAsync(client);
        await RunCycleAsync();
        time.Advance(SimulatedDelay - TimeSpan.FromSeconds(1));

        var result = await RunCycleAsync();

        Assert.Equal(new PaymentProcessingCycleResult(Claimed: 0, Finished: 0), result);
        Assert.Equal("Processing", (await PaymentRequests.GetAsync(client, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunOnce_AfterSimulatedDelay_CompletesWithProviderReference()
    {
        var id = await PaymentRequests.CreateAsync(client);
        Assert.Equal("Pending", (await PaymentRequests.GetAsync(client, id)).GetProperty("status").GetString());
        await RunCycleAsync();
        time.Advance(SimulatedDelay);

        var result = await RunCycleAsync();

        Assert.Equal(new PaymentProcessingCycleResult(Claimed: 0, Finished: 1), result);
        var payment = await PaymentRequests.GetAsync(client, id);
        Assert.Equal("Completed", payment.GetProperty("status").GetString());
        Assert.Matches(ProviderReferencePattern(), payment.GetProperty("providerReference").GetString());
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("failureReason").ValueKind);
        Assert.Equal(time.GetUtcNow(), payment.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("FAIL Teste", "BR", 100.00, "BENEFICIARY_REJECTED")]
    [InlineData("Kim", "KP", 100.00, "COMPLIANCE_BLOCKED")]
    [InlineData("Ali", "IR", 100.00, "COMPLIANCE_BLOCKED")]
    [InlineData("Maria Silva", "BR", 100000.01, "LIMIT_EXCEEDED")]
    public async Task RunOnce_PaymentMatchingFailureRule_FailsWithReason(
        string beneficiaryName,
        string country,
        decimal foreignAmount,
        string expectedReason)
    {
        var id = await PaymentRequests.CreateAsync(client, beneficiaryName, country, foreignAmount);
        await RunCycleAsync();
        time.Advance(SimulatedDelay);

        await RunCycleAsync();

        var payment = await PaymentRequests.GetAsync(client, id);
        Assert.Equal("Failed", payment.GetProperty("status").GetString());
        Assert.Equal(expectedReason, payment.GetProperty("failureReason").GetString());
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("providerReference").ValueKind);
    }

    [Fact]
    public async Task RunOnce_MoreThanBatchSizePending_ClaimsAtMostTenPerCycle()
    {
        for (var i = 0; i < 12; i++)
        {
            await PaymentRequests.CreateAsync(client);
        }

        var first = await RunCycleAsync();
        var second = await RunCycleAsync();

        Assert.Equal(PaymentProcessingCycle.BatchSize, first.Claimed);
        Assert.Equal(2, second.Claimed);
    }

    [Fact]
    public async Task RunOnce_ConcurrentCycles_ClaimEachPaymentOnlyOnce()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 15; i++)
        {
            ids.Add(await PaymentRequests.CreateAsync(client));
        }

        // Dois ciclos ao mesmo tempo, como duas réplicas do serviço: o SKIP LOCKED faz cada uma pegar linhas diferentes.
        var results = await Task.WhenAll(RunCycleAsync(), RunCycleAsync());

        Assert.Equal(ids.Count, results.Sum(r => r.Claimed));
        foreach (var id in ids)
        {
            Assert.Equal("Processing", (await PaymentRequests.GetAsync(client, id)).GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task RunOnce_OnePaymentFailsToSave_ProcessesTheOthers()
    {
        var broken = await PaymentRequests.CreateAsync(client, beneficiaryName: "Erro Simulado");
        time.Advance(TimeSpan.FromSeconds(1));
        var healthy = await PaymentRequests.CreateAsync(client);

        // Trigger que faz o UPDATE do primeiro pagamento do lote falhar no banco.
        await ExecuteSqlAsync(
            """
            CREATE FUNCTION fail_simulated_payment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.beneficiary_name = 'Erro Simulado' THEN
                    RAISE EXCEPTION 'falha simulada ao gravar o pagamento';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER fail_simulated_payment BEFORE UPDATE ON payments
                FOR EACH ROW EXECUTE FUNCTION fail_simulated_payment();
            """);

        var result = await RunCycleAsync();

        Assert.Equal(1, result.Claimed);
        Assert.Equal("Pending", (await PaymentRequests.GetAsync(client, broken)).GetProperty("status").GetString());
        Assert.Equal("Processing", (await PaymentRequests.GetAsync(client, healthy)).GetProperty("status").GetString());
    }

    private async Task<PaymentProcessingCycleResult> RunCycleAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var cycle = scope.ServiceProvider.GetRequiredService<PaymentProcessingCycle>();
        return await cycle.RunOnceAsync(TestContext.Current.CancellationToken);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
    }

    [GeneratedRegex("^MOCK-[A-Z0-9]{8}$")]
    private static partial Regex ProviderReferencePattern();
}
