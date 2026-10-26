using System.Text.RegularExpressions;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.UnitTests.Domain;

public partial class MockPaymentOutcomePolicyTests
{
    private const decimal MaxAmount = 100000m;

    [Fact]
    public void Decide_RegularPayment_ReturnsCompletedWithMockReference()
    {
        var outcome = CreatePolicy().Decide(TestPayments.Create());

        Assert.Equal(PaymentStatus.Completed, outcome.Status);
        Assert.Null(outcome.FailureReason);
        Assert.Matches(ProviderReferencePattern(), outcome.ProviderReference);
    }

    [Fact]
    public void Decide_SameRandomSeed_GeneratesSameReference()
    {
        var first = CreatePolicy(new Random(42)).Decide(TestPayments.Create());
        var second = CreatePolicy(new Random(42)).Decide(TestPayments.Create());

        Assert.Equal(first.ProviderReference, second.ProviderReference);
    }

    [Fact]
    public void Decide_ManyPayments_AlwaysGeneratesPrefixPlusEightAlphanumerics()
    {
        var policy = CreatePolicy(new Random(7));

        var references = Enumerable.Range(0, 500)
            .Select(_ => policy.Decide(TestPayments.Create()).ProviderReference)
            .ToList();

        Assert.All(references, reference => Assert.Matches(ProviderReferencePattern(), reference));
        Assert.True(references.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData("FAIL Teste")]
    [InlineData("fail")]
    [InlineData("Maria Failla")]
    [InlineData("xxFaIlxx")]
    public void Decide_NameContainsFailIgnoringCase_ReturnsBeneficiaryRejected(string beneficiaryName)
    {
        var outcome = CreatePolicy().Decide(TestPayments.Create(beneficiaryName: beneficiaryName));

        Assert.Equal(PaymentStatus.Failed, outcome.Status);
        Assert.Equal(PaymentFailureReasons.BeneficiaryRejected, outcome.FailureReason);
        Assert.Null(outcome.ProviderReference);
    }

    [Theory]
    [InlineData("KP")]
    [InlineData("IR")]
    public void Decide_BlockedCountry_ReturnsComplianceBlocked(string country)
    {
        var outcome = CreatePolicy().Decide(TestPayments.Create(beneficiaryCountry: country));

        Assert.Equal(PaymentStatus.Failed, outcome.Status);
        Assert.Equal(PaymentFailureReasons.ComplianceBlocked, outcome.FailureReason);
        Assert.Null(outcome.ProviderReference);
    }

    [Fact]
    public void Decide_AmountAboveMax_ReturnsLimitExceeded()
    {
        var outcome = CreatePolicy().Decide(TestPayments.Create(foreignAmount: 100000.01m));

        Assert.Equal(PaymentStatus.Failed, outcome.Status);
        Assert.Equal(PaymentFailureReasons.LimitExceeded, outcome.FailureReason);
        Assert.Null(outcome.ProviderReference);
    }

    [Fact]
    public void Decide_AmountEqualToMax_ReturnsCompleted()
    {
        var outcome = CreatePolicy().Decide(TestPayments.Create(foreignAmount: MaxAmount));

        Assert.Equal(PaymentStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Decide_FailNameAndBlockedCountryAndAmountAboveMax_NameRuleWins()
    {
        var payment = TestPayments.Create(beneficiaryName: "FAIL", beneficiaryCountry: "KP", foreignAmount: 200000m);

        var outcome = CreatePolicy().Decide(payment);

        Assert.Equal(PaymentFailureReasons.BeneficiaryRejected, outcome.FailureReason);
    }

    [Fact]
    public void Decide_BlockedCountryAndAmountAboveMax_CountryRuleWins()
    {
        var payment = TestPayments.Create(beneficiaryCountry: "IR", foreignAmount: 200000m);

        var outcome = CreatePolicy().Decide(payment);

        Assert.Equal(PaymentFailureReasons.ComplianceBlocked, outcome.FailureReason);
    }

    [Fact]
    public void Decide_BlockedCountriesWithSpacesAndLowercase_StillBlocksCountry()
    {
        var policy = new MockPaymentOutcomePolicy([" kp ", "ir"], MaxAmount, new Random(1));

        Assert.Equal(PaymentFailureReasons.ComplianceBlocked, policy.Decide(TestPayments.Create(beneficiaryCountry: "KP")).FailureReason);
        Assert.Equal(PaymentFailureReasons.ComplianceBlocked, policy.Decide(TestPayments.Create(beneficiaryCountry: "IR")).FailureReason);
    }

    [Fact]
    public void Decide_NoBlockedCountries_DoesNotBlockAnyCountry()
    {
        var policy = new MockPaymentOutcomePolicy([], MaxAmount, new Random(1));

        var outcome = policy.Decide(TestPayments.Create(beneficiaryCountry: "KP"));

        Assert.Equal(PaymentStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Decide_CustomMaxAmount_UsesConfiguredLimit()
    {
        var policy = new MockPaymentOutcomePolicy(["KP"], maxAmount: 500m, new Random(1));

        Assert.Equal(PaymentStatus.Completed, policy.Decide(TestPayments.Create(foreignAmount: 500m)).Status);
        Assert.Equal(PaymentFailureReasons.LimitExceeded, policy.Decide(TestPayments.Create(foreignAmount: 500.01m)).FailureReason);
    }

    private static MockPaymentOutcomePolicy CreatePolicy(Random? random = null) =>
        new(["KP", "IR"], MaxAmount, random ?? new Random(123));

    [GeneratedRegex("^MOCK-[A-Z0-9]{8}$")]
    private static partial Regex ProviderReferencePattern();
}
