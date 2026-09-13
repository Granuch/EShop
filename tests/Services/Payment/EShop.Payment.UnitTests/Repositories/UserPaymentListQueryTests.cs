using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.UnitTests.Repositories;

/// <summary>
/// Payment audit Stage 10 (M7, D11). Checks the SQL the per-user list sends, not the rows it returns. The tie-break on
/// <c>Id</c> matters only where Postgres may reorder rows sharing a timestamp between pages, and InMemory's stable sort
/// never shows that: Ordering's audit found a row-based test that stayed green with its tie-break removed. No
/// connection is opened.
/// </summary>
[TestFixture]
public class UserPaymentListQueryTests
{
    [Test]
    public void TheUserList_IsNewestFirst_WithIdBreakingTies_AndSkipsPlaceholders()
    {
        using var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=unused;Username=unused;Password=unused")
            .Options);

        var sql = PaymentRepository.ForUserList(db.PaymentTransactions, "user-1").ToQueryString();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Match(@"ORDER BY \w+\.""CreatedAt"" DESC, \w+\.""Id"" DESC"), sql);
            Assert.That(sql, Does.Contain("\"PaymentMethod\" <> 'None'"), sql);
        });
    }
}
