using System.Net.Http.Json;
using System.Text.Json;

namespace MiniCambio.Payments.IntegrationTests;

/// <summary>Corpos de requisição e atalhos HTTP usados pelos testes.</summary>
internal static class PaymentRequests
{
    public const string Route = "/api/v1/payments";

    public static object Body(
        Guid orderId,
        string beneficiaryName = "Maria Silva",
        string beneficiaryCountry = "BR",
        decimal foreignAmount = 1500.50m) => new
        {
            orderId,
            currency = "USD",
            foreignAmount,
            beneficiary = new { name = beneficiaryName, country = beneficiaryCountry, accountNumber = "12345-6" },
        };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(Route, body, TestContext.Current.CancellationToken);

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static async Task<Guid> CreateAsync(
        HttpClient client,
        string beneficiaryName = "Maria Silva",
        string beneficiaryCountry = "BR",
        decimal foreignAmount = 1500.50m)
    {
        var response = await PostAsync(client, Body(Guid.NewGuid(), beneficiaryName, beneficiaryCountry, foreignAmount));
        response.EnsureSuccessStatusCode();
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    public static Task<JsonElement> GetAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<JsonElement>($"{Route}/{id}", TestContext.Current.CancellationToken);
}
