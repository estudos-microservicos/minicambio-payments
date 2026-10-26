using Microsoft.EntityFrameworkCore;
using MiniCambio.Payments.Api.Application;
using MiniCambio.Payments.Api.Domain;
using MiniCambio.Payments.Api.Infrastructure.Persistence.Configurations;
using Npgsql;

namespace MiniCambio.Payments.Api.Infrastructure.Persistence;

internal sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public async Task<bool> TryAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, // 23505
            ConstraintName: PaymentConfiguration.OrderIdUniqueIndex,
        })
        {
            // Outra requisição gravou o pagamento da mesma ordem primeiro. Tira a entidade recusada do change
            // tracker para que o contexto continue utilizável na consulta que busca o pagamento existente.
            db.Entry(payment).State = EntityState.Detached;
            return false;
        }
    }
}
