using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Data;

namespace OsbbBilling.Web.Services;

public class PaymentRegistrationService(OsbbDbContext database)
{
    public async Task RegisterAsync(int accountId, DateOnly paymentDate, decimal amount, string reference,
        int createdBy, CancellationToken cancellationToken)
    {
        if (!await SqlReadRetry.ExecuteAsync(() => database.Accounts.AnyAsync(x => x.AccountId == accountId,
            cancellationToken), cancellationToken))
            throw new CatalogRuleException("Особового рахунку не знайдено.", "AccountId");
        reference = reference.Trim();
        if (await SqlReadRetry.ExecuteAsync(() => database.Payments.AnyAsync(x => x.Reference == reference,
            cancellationToken), cancellationToken))
            throw new CatalogRuleException("Оплату з таким номером документа вже зареєстровано.", "Reference");

        var parameters = new object[]
        {
            new SqlParameter("@AccountId", SqlDbType.Int) { Value = accountId },
            new SqlParameter("@PaymentDate", SqlDbType.Date) { Value = paymentDate.ToDateTime(TimeOnly.MinValue) },
            new SqlParameter("@Amount", SqlDbType.Decimal) { Precision = 19, Scale = 2, Value = amount },
            new SqlParameter("@Reference", SqlDbType.NVarChar, 100) { Value = reference },
            new SqlParameter("@CreatedBy", SqlDbType.Int) { Value = createdBy }
        };
        database.Database.SetCommandTimeout(60);
        // Процедура реєструє оплату та розподіляє її в одній транзакції.
        await database.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_RegisterPayment @AccountId=@AccountId, @PaymentDate=@PaymentDate, @Amount=@Amount, @Reference=@Reference, @CreatedBy=@CreatedBy",
            parameters, cancellationToken);
    }
}
