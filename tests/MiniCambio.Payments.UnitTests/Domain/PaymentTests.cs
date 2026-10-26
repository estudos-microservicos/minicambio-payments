using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.UnitTests.Domain;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = TestPayments.Now;

    [Fact]
    public void Create_ValidData_StartsPendingWithTimestamps()
    {
        var orderId = Guid.NewGuid();

        var payment = Payment.Create(orderId, "EUR", 250.75m, "Maria Silva", "PT", "PT50000201231234567890154", Now);

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal("EUR", payment.Currency);
        Assert.Equal(250.75m, payment.ForeignAmount);
        Assert.Equal("Maria Silva", payment.BeneficiaryName);
        Assert.Equal("PT", payment.BeneficiaryCountry);
        Assert.Equal("PT50000201231234567890154", payment.BeneficiaryAccount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.FailureReason);
        Assert.Null(payment.ProviderReference);
        Assert.Null(payment.ProcessingStartedAt);
        Assert.Equal(Now, payment.CreatedAt);
        Assert.Equal(Now, payment.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveAmount_ThrowsDomainException(decimal foreignAmount)
    {
        Assert.Throws<DomainException>(() => TestPayments.Create(foreignAmount: foreignAmount));
    }

    [Fact]
    public void Create_EmptyOrderId_ThrowsDomainException()
    {
        Assert.Throws<DomainException>(() => TestPayments.Create(orderId: Guid.Empty));
    }

    [Fact]
    public void StartProcessing_Pending_MovesToProcessingAndSetsStartedAt()
    {
        var payment = TestPayments.Create();
        var later = Now.AddSeconds(5);

        payment.StartProcessing(later);

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(later, payment.ProcessingStartedAt);
        Assert.Equal(later, payment.UpdatedAt);
        Assert.Equal(Now, payment.CreatedAt);
    }

    [Theory]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    public void StartProcessing_NotPending_ThrowsDomainException(PaymentStatus status)
    {
        var payment = TestPayments.InStatus(status);

        var exception = Assert.Throws<DomainException>(() => payment.StartProcessing(Now));

        Assert.Contains(status.ToString(), exception.Message);
        Assert.Equal(status, payment.Status);
    }

    [Fact]
    public void Complete_Processing_MovesToCompletedWithReference()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);
        var later = Now.AddSeconds(3);

        payment.Complete("MOCK-ZX81QW09", later);

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MOCK-ZX81QW09", payment.ProviderReference);
        Assert.Null(payment.FailureReason);
        Assert.Equal(later, payment.UpdatedAt);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    public void Complete_NotProcessing_ThrowsDomainException(PaymentStatus status)
    {
        var payment = TestPayments.InStatus(status);

        Assert.Throws<DomainException>(() => payment.Complete("MOCK-ZX81QW09", Now));
        Assert.Equal(status, payment.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Complete_BlankReference_ThrowsArgumentException(string providerReference)
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);

        Assert.ThrowsAny<ArgumentException>(() => payment.Complete(providerReference, Now));
        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    [Fact]
    public void Fail_Processing_MovesToFailedWithReason()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);
        var later = Now.AddSeconds(3);

        payment.Fail(PaymentFailureReasons.LimitExceeded, later);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentFailureReasons.LimitExceeded, payment.FailureReason);
        Assert.Null(payment.ProviderReference);
        Assert.Equal(later, payment.UpdatedAt);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Completed)]
    [InlineData(PaymentStatus.Failed)]
    public void Fail_NotProcessing_ThrowsDomainException(PaymentStatus status)
    {
        var payment = TestPayments.InStatus(status);

        Assert.Throws<DomainException>(() => payment.Fail(PaymentFailureReasons.ComplianceBlocked, Now));
        Assert.Equal(status, payment.Status);
    }

    [Fact]
    public void Fail_BlankReason_ThrowsArgumentException()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);

        Assert.ThrowsAny<ArgumentException>(() => payment.Fail(" ", Now));
    }

    [Fact]
    public void ApplyOutcome_Completed_CompletesPayment()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);

        payment.ApplyOutcome(PaymentOutcome.Completed("MOCK-00000000"), Now);

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal("MOCK-00000000", payment.ProviderReference);
    }

    [Fact]
    public void ApplyOutcome_Failed_FailsPayment()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Processing);

        payment.ApplyOutcome(PaymentOutcome.Failed(PaymentFailureReasons.BeneficiaryRejected), Now);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(PaymentFailureReasons.BeneficiaryRejected, payment.FailureReason);
    }

    [Fact]
    public void ApplyOutcome_PaymentNotProcessing_ThrowsDomainException()
    {
        var payment = TestPayments.InStatus(PaymentStatus.Pending);

        Assert.Throws<DomainException>(() => payment.ApplyOutcome(PaymentOutcome.Completed("MOCK-00000000"), Now));
    }
}
