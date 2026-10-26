using MiniCambio.Payments.Api.Application.Payments;

namespace MiniCambio.Payments.UnitTests.Application;

public class GetPaymentByOrderQueryValidatorTests
{
    private readonly GetPaymentByOrderQueryValidator validator = new();

    [Fact]
    public void Validate_ValidGuid_HasNoErrors()
    {
        var result = validator.Validate(new GetPaymentByOrderQuery(Guid.NewGuid().ToString()));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Validate_MissingOrInvalidOrderId_HasSingleOrderIdError(string? orderId)
    {
        var result = validator.Validate(new GetPaymentByOrderQuery(orderId));

        var error = Assert.Single(result.Errors);
        Assert.Equal("orderId", error.PropertyName, ignoreCase: true);
    }
}
