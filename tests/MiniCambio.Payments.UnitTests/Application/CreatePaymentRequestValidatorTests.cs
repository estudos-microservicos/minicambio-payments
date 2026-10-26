using MiniCambio.Payments.Api.Application.Payments;

namespace MiniCambio.Payments.UnitTests.Application;

public class CreatePaymentRequestValidatorTests
{
    private readonly CreatePaymentRequestValidator validator = new();

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = validator.Validate(ValidRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MaximumLengths_HasNoErrors()
    {
        var request = ValidRequest() with
        {
            Beneficiary = new BeneficiaryRequest(new string('a', 120), "BR", new string('9', 34)),
        };

        Assert.True(validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Validate_MissingOrderId_HasOrderIdError(string? orderId)
    {
        var request = ValidRequest() with { OrderId = orderId is null ? null : Guid.Parse(orderId) };

        AssertSingleErrorFor(request, "orderId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDT")]
    public void Validate_InvalidCurrency_HasCurrencyError(string? currency)
    {
        AssertSingleErrorFor(ValidRequest() with { Currency = currency }, "currency");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(10.123)]
    public void Validate_InvalidAmount_HasForeignAmountError(double? amount)
    {
        AssertSingleErrorFor(ValidRequest() with { ForeignAmount = (decimal?)amount }, "foreignAmount");
    }

    [Fact]
    public void Validate_MissingBeneficiary_HasBeneficiaryErrorOnly()
    {
        var result = validator.Validate(ValidRequest() with { Beneficiary = null });

        var error = Assert.Single(result.Errors);
        Assert.Equal("beneficiary", error.PropertyName, ignoreCase: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingBeneficiaryName_HasBeneficiaryNameError(string? name)
    {
        AssertSingleErrorFor(ValidRequest() with { Beneficiary = new BeneficiaryRequest(name, "BR", "12345-6") }, "beneficiary.name");
    }

    [Fact]
    public void Validate_BeneficiaryNameTooLong_HasBeneficiaryNameError()
    {
        var name = new string('a', 121);

        AssertSingleErrorFor(ValidRequest() with { Beneficiary = new BeneficiaryRequest(name, "BR", "12345-6") }, "beneficiary.name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("br")]
    [InlineData("BRA")]
    [InlineData("B1")]
    public void Validate_InvalidBeneficiaryCountry_HasBeneficiaryCountryError(string? country)
    {
        AssertSingleErrorFor(
            ValidRequest() with { Beneficiary = new BeneficiaryRequest("Maria Silva", country, "12345-6") },
            "beneficiary.country");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Validate_MissingBeneficiaryAccount_HasBeneficiaryAccountNumberError(string? accountNumber)
    {
        AssertSingleErrorFor(
            ValidRequest() with { Beneficiary = new BeneficiaryRequest("Maria Silva", "BR", accountNumber) },
            "beneficiary.accountNumber");
    }

    [Fact]
    public void Validate_BeneficiaryAccountTooLong_HasBeneficiaryAccountNumberError()
    {
        var accountNumber = new string('9', 35);

        AssertSingleErrorFor(
            ValidRequest() with { Beneficiary = new BeneficiaryRequest("Maria Silva", "BR", accountNumber) },
            "beneficiary.accountNumber");
    }

    /// <summary>
    /// Exatamente um erro, no campo esperado (uma mensagem por campo). O nome é comparado sem diferenciar
    /// maiúsculas porque o camelCase vem do resolver global registrado em <c>AddApplication</c>.
    /// </summary>
    private void AssertSingleErrorFor(CreatePaymentRequest request, string propertyName)
    {
        var result = validator.Validate(request);

        var error = Assert.Single(result.Errors);
        Assert.Equal(propertyName, error.PropertyName, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(error.ErrorMessage));
    }

    private static CreatePaymentRequest ValidRequest() =>
        new(Guid.NewGuid(), "USD", 1500.50m, new BeneficiaryRequest("Maria Silva", "BR", "12345-6"));
}
