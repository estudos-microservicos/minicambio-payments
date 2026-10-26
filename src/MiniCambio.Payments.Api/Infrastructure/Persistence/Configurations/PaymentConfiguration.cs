using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniCambio.Payments.Api.Application.Payments;
using MiniCambio.Payments.Api.Domain;

namespace MiniCambio.Payments.Api.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <summary>Índice único que garante um pagamento por ordem (a idempotência sob concorrência depende dele).</summary>
    public const string OrderIdUniqueIndex = "ix_payments_order_id";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => x.OrderId).IsUnique().HasDatabaseName(OrderIdUniqueIndex);

        // O worker filtra por status a cada ciclo.
        builder.HasIndex(x => x.Status);

        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.ForeignAmount).HasPrecision(18, 2);
        builder.Property(x => x.BeneficiaryName).HasMaxLength(CreatePaymentRequestValidator.BeneficiaryNameMaxLength);
        builder.Property(x => x.BeneficiaryCountry).HasMaxLength(2);
        builder.Property(x => x.BeneficiaryAccount).HasMaxLength(CreatePaymentRequestValidator.BeneficiaryAccountMaxLength);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FailureReason).HasMaxLength(50);
        builder.Property(x => x.ProviderReference).HasMaxLength(50);
    }
}
