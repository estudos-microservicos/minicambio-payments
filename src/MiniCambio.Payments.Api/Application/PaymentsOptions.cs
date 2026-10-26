using System.ComponentModel.DataAnnotations;

namespace MiniCambio.Payments.Api.Application;

/// <summary>Seção <c>Payments</c> da configuração (ex.: <c>Payments__WorkerIntervalSeconds</c>).</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Intervalo entre os ciclos do worker de processamento, em segundos.</summary>
    [Range(1, 3600)]
    public int WorkerIntervalSeconds { get; set; } = 5;

    /// <summary>Tempo mínimo que um pagamento fica em <c>Processing</c> antes de receber o resultado, em segundos.</summary>
    [Range(0, 3600)]
    public int SimulatedDelaySeconds { get; set; } = 3;

    /// <summary>
    /// Países bloqueados (ISO 3166-1 alpha-2) separados por vírgula, ex.: <c>KP,IR</c>. Vazio = nenhum bloqueio.
    /// Fica como texto para ser fácil de definir numa única variável de ambiente.
    /// </summary>
    [RegularExpression(
        @"^\s*(?:[A-Za-z]{2}\s*)?(?:,\s*(?:[A-Za-z]{2}\s*)?)*$",
        ErrorMessage = "Payments:BlockedCountries deve ser uma lista de códigos de país com 2 letras separados por vírgula (ex.: KP,IR).")]
    public string BlockedCountries { get; set; } = "KP,IR";

    /// <summary>Valor máximo (em moeda estrangeira) aceito pelo mock; acima disso o pagamento é recusado.</summary>
    [Range(typeof(decimal), "0.01", "9999999999999999.99", ParseLimitsInInvariantCulture = true)]
    public decimal MaxAmount { get; set; } = 100000m;

    /// <summary>Lista normalizada (sem espaços, em maiúsculas) de <see cref="BlockedCountries"/>.</summary>
    public IReadOnlyList<string> GetBlockedCountries() =>
    [
        .. BlockedCountries
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(country => country.ToUpperInvariant()),
    ];
}
