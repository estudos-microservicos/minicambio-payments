using FluentValidation;

namespace MiniCambio.Payments.Api.Application.Payments;

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public const int BeneficiaryNameMaxLength = 120;
    public const int BeneficiaryAccountMaxLength = 34;

    public CreatePaymentRequestValidator()
    {
        // Uma mensagem por campo: a primeira regra que falhar interrompe as demais do mesmo campo.
        RuleLevelCascadeMode = CascadeMode.Stop;

        // NotEmpty não recusa Guid.Empty em Guid? (o "vazio" de um anulável é null), daí as duas regras.
        RuleFor(x => x.OrderId)
            .NotNull().WithMessage("Informe o id da ordem.")
            .NotEqual(Guid.Empty).WithMessage("Informe o id da ordem.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Informe a moeda.")
            .Matches("^[A-Z]{3}$").WithMessage("A moeda deve ser um código ISO 4217 com 3 letras maiúsculas (ex.: USD).");

        RuleFor(x => x.ForeignAmount)
            .NotNull().WithMessage("Informe o valor.")
            .GreaterThan(0).WithMessage("O valor deve ser maior que zero.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage("O valor deve ter no máximo 2 casas decimais.");

        RuleFor(x => x.Beneficiary)
            .NotNull().WithMessage("Informe o beneficiário.");

        // Erros dos campos aninhados saem como "beneficiary.name", "beneficiary.country" e "beneficiary.accountNumber".
        When(x => x.Beneficiary is not null, () =>
        {
            RuleFor(x => x.Beneficiary!.Name)
                .NotEmpty().WithMessage("Informe o nome do beneficiário.")
                .MaximumLength(BeneficiaryNameMaxLength)
                .WithMessage($"O nome do beneficiário deve ter no máximo {BeneficiaryNameMaxLength} caracteres.");

            RuleFor(x => x.Beneficiary!.Country)
                .NotEmpty().WithMessage("Informe o país do beneficiário.")
                .Matches("^[A-Z]{2}$")
                .WithMessage("O país deve ser um código ISO 3166-1 alpha-2 com 2 letras maiúsculas (ex.: BR).");

            RuleFor(x => x.Beneficiary!.AccountNumber)
                .NotEmpty().WithMessage("Informe a conta do beneficiário.")
                .MaximumLength(BeneficiaryAccountMaxLength)
                .WithMessage($"A conta do beneficiário deve ter no máximo {BeneficiaryAccountMaxLength} caracteres.");
        });
    }
}
