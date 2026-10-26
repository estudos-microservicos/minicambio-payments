using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>Health checks, OpenAPI e correlation id.</summary>
public class PlatformEndpointsTests(PaymentsApiFactory factory)
{
    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthChecks_DatabaseUp_ReturnHealthy(string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenApi_InDevelopment_DocumentsOrderIdQueryParameterInCamelCase()
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);

        var parameters = document.GetProperty("paths").GetProperty(PaymentRequests.Route).GetProperty("get").GetProperty("parameters");
        var parameter = Assert.Single(parameters.EnumerateArray());
        Assert.Equal("orderId", parameter.GetProperty("name").GetString());
        Assert.Equal("query", parameter.GetProperty("in").GetString());
    }

    [Fact]
    public async Task Request_WithCorrelationId_EchoesHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{PaymentRequests.Route}/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-Id", "teste-123");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("teste-123", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task Request_WithoutCorrelationId_GeneratesOne()
    {
        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.True(Guid.TryParse(Assert.Single(response.Headers.GetValues("X-Correlation-Id")), out _));
    }
}
