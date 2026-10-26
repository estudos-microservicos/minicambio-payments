using Microsoft.Extensions.DependencyInjection;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.IntegrationTests;

public class PaymentRepositoryTests(PaymentsApiFactory factory)
{
    [Fact]
    public async Task TryAddAsync_RaceForSameOrder_SecondInsertReturnsFalseAndKeepsWinner()
    {
        var orderId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Duas "requisições" (dois escopos, dois DbContexts) que já passaram pela verificação de existência.
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        Assert.Null(await first.GetByOrderIdAsync(orderId, cancellationToken));
        Assert.Null(await second.GetByOrderIdAsync(orderId, cancellationToken));

        var winner = NewPayment(orderId);
        var loser = NewPayment(orderId);
        Assert.True(await first.TryAddAsync(winner, cancellationToken));
        Assert.False(await second.TryAddAsync(loser, cancellationToken));

        // O contexto que perdeu a corrida continua utilizável e enxerga o pagamento vencedor.
        Assert.Equal(winner.Id, (await second.GetByOrderIdAsync(orderId, cancellationToken))?.Id);
        Assert.Null(await second.GetAsync(loser.Id, cancellationToken));
    }

    private static Payment NewPayment(Guid orderId) =>
        Payment.Create(orderId, "EUR", 10m, "Maria Silva", "PT", "12345-6", new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero));
}
