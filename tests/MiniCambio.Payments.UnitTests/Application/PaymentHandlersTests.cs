using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.UnitTests.Application;

public class PaymentHandlersTests
{
    private readonly FakeTimeProvider time = new(TestPayments.Now);
    private readonly InMemoryPaymentRepository payments = new();

    [Fact]
    public async Task CreatePayment_NewOrder_PersistsPendingPaymentAndReturnsCreated()
    {
        var orderId = Guid.NewGuid();

        var result = await CreateHandler().HandleAsync(Request(orderId), CancellationToken.None);

        Assert.True(result.Created);
        var stored = Assert.Single(payments.Payments);
        Assert.Equal(stored.Id, result.Payment.Id);
        Assert.Equal(orderId, result.Payment.OrderId);
        Assert.Equal(PaymentStatus.Pending, result.Payment.Status);
        Assert.Equal("USD", result.Payment.Currency);
        Assert.Equal(1500.50m, result.Payment.ForeignAmount);
        Assert.Equal(new BeneficiaryResponse("Maria Silva", "BR", "12345-6"), result.Payment.Beneficiary);
        Assert.Equal(time.GetUtcNow(), result.Payment.CreatedAt);
        Assert.Equal(time.GetUtcNow(), result.Payment.UpdatedAt);
        Assert.Null(result.Payment.ProcessingStartedAt);
        Assert.False(string.IsNullOrWhiteSpace(result.Payment.ServedBy));
    }

    [Fact]
    public async Task CreatePayment_BeneficiaryWithSurroundingSpaces_TrimsNameAndAccount()
    {
        var request = Request(Guid.NewGuid()) with
        {
            Beneficiary = new BeneficiaryRequest("  Maria Silva ", "BR", " 12345-6 "),
        };

        var result = await CreateHandler().HandleAsync(request, CancellationToken.None);

        Assert.Equal("Maria Silva", result.Payment.Beneficiary.Name);
        Assert.Equal("12345-6", result.Payment.Beneficiary.AccountNumber);
    }

    [Fact]
    public async Task CreatePayment_OrderAlreadyHasPayment_ReturnsExistingWithoutCreating()
    {
        var orderId = Guid.NewGuid();
        var first = await CreateHandler().HandleAsync(Request(orderId), CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));

        var second = await CreateHandler().HandleAsync(Request(orderId, foreignAmount: 99m), CancellationToken.None);

        Assert.False(second.Created);
        Assert.Equal(first.Payment.Id, second.Payment.Id);
        Assert.Equal(1500.50m, second.Payment.ForeignAmount);
        Assert.Single(payments.Payments);
    }

    [Fact]
    public async Task CreatePayment_ConcurrentInsertForSameOrder_ReturnsWinningPayment()
    {
        var orderId = Guid.NewGuid();
        var winner = TestPayments.Create(orderId: orderId);
        payments.ConcurrentInsert = winner;

        var result = await CreateHandler().HandleAsync(Request(orderId), CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(winner.Id, result.Payment.Id);
        Assert.Single(payments.Payments);
    }

    [Fact]
    public async Task GetPayment_Existing_ReturnsPayment()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Completed);
        payments.Payments.Add(payment);

        var response = await new GetPaymentHandler(payments).HandleAsync(payment.Id, CancellationToken.None);

        Assert.Equal(payment.Id, response.Id);
        Assert.Equal(PaymentStatus.Completed, response.Status);
        Assert.Equal("MOCK-ABCD1234", response.ProviderReference);
        Assert.Equal(TestPayments.Now, response.ProcessingStartedAt);
    }

    [Fact]
    public async Task GetPayment_UnknownId_ThrowsResourceNotFound()
    {
        var handler = new GetPaymentHandler(payments);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetPaymentByOrder_Existing_ReturnsPayment()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Failed);
        payments.Payments.Add(payment);

        var response = await new GetPaymentByOrderHandler(payments).HandleAsync(payment.OrderId, CancellationToken.None);

        Assert.Equal(payment.Id, response.Id);
        Assert.Equal(PaymentStatus.Failed, response.Status);
        Assert.Equal(PaymentFailureReasons.BeneficiaryRejected, response.FailureReason);
    }

    [Fact]
    public async Task GetPaymentByOrder_UnknownOrder_ThrowsResourceNotFound()
    {
        var handler = new GetPaymentByOrderHandler(payments);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private CreatePaymentHandler CreateHandler() =>
        new(payments, time, NullLogger<CreatePaymentHandler>.Instance);

    private static CreatePaymentRequest Request(Guid orderId, decimal foreignAmount = 1500.50m) =>
        new(orderId, "USD", foreignAmount, new BeneficiaryRequest("Maria Silva", "BR", "12345-6"));
}
