using System.Text.Json;
using FluentValidation;
using FluentValidation.Internal;
using Microsoft.Extensions.Options;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<PaymentsOptions>()
            .BindConfiguration(PaymentsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Nomes de campo nos erros seguem o JSON (camelCase), para o cliente mapear cada erro ao seu campo.
        ValidatorOptions.Global.PropertyNameResolver = (_, member, expression) =>
        {
            var path = expression is null ? member?.Name : PropertyChain.FromExpression(expression).ToString();
            return path is null ? null : string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
        };
        services.AddValidatorsFromAssemblyContaining<CreatePaymentRequestValidator>();

        // Política pura do domínio, configurada a partir das options. Random.Shared é thread-safe (a política é
        // singleton e usada por ciclos concorrentes); os testes injetam um Random com semente fixa.
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PaymentsOptions>>().Value;
            return new MockPaymentOutcomePolicy(options.GetBlockedCountries(), options.MaxAmount, Random.Shared);
        });

        services.AddScoped<CreatePaymentHandler>();
        services.AddScoped<GetPaymentHandler>();
        services.AddScoped<GetPaymentByOrderHandler>();

        return services;
    }
}
