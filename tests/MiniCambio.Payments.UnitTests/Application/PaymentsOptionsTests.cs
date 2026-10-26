using System.ComponentModel.DataAnnotations;
using MiniCambio.Payments.Api.Application;

namespace MiniCambio.Payments.UnitTests.Application;

public class PaymentsOptionsTests
{
    [Fact]
    public void Defaults_MatchSpecAndAreValid()
    {
        var options = new PaymentsOptions();

        Assert.Equal(5, options.WorkerIntervalSeconds);
        Assert.Equal(3, options.SimulatedDelaySeconds);
        Assert.Equal(["KP", "IR"], options.GetBlockedCountries());
        Assert.Equal(100000m, options.MaxAmount);
        Assert.Empty(Validate(options));
    }

    public static TheoryData<string, string[]> BlockedCountriesCases => new()
    {
        { " kp , ir ,", ["KP", "IR"] },
        { "US", ["US"] },
        { "", [] },
    };

    [Theory]
    [MemberData(nameof(BlockedCountriesCases))]
    public void GetBlockedCountries_CommaSeparatedText_ReturnsNormalizedCodes(string blockedCountries, string[] expected)
    {
        var options = new PaymentsOptions { BlockedCountries = blockedCountries };

        Assert.Equal(expected, options.GetBlockedCountries());
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("KPX")]
    [InlineData("KP;IR")]
    [InlineData("K1")]
    public void Validate_MalformedBlockedCountries_IsInvalid(string blockedCountries)
    {
        var errors = Validate(new PaymentsOptions { BlockedCountries = blockedCountries });

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(PaymentsOptions.BlockedCountries)));
    }

    [Fact]
    public void Validate_OutOfRangeValues_IsInvalid()
    {
        var options = new PaymentsOptions { WorkerIntervalSeconds = 0, SimulatedDelaySeconds = -1, MaxAmount = 0m };

        var invalidMembers = Validate(options).SelectMany(e => e.MemberNames).ToList();

        Assert.Contains(nameof(PaymentsOptions.WorkerIntervalSeconds), invalidMembers);
        Assert.Contains(nameof(PaymentsOptions.SimulatedDelaySeconds), invalidMembers);
        Assert.Contains(nameof(PaymentsOptions.MaxAmount), invalidMembers);
    }

    private static List<ValidationResult> Validate(PaymentsOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
