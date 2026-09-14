using System.Net;
using System.Text.RegularExpressions;
using EShop.Ordering.IntegrationTests.Fixtures;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.Persistence;

/// <summary>
/// Ordering audit M7, the guard that actually guards it. Both order lists must sort by
/// <c>(CreatedAt, Id)</c>: <c>CreatedAt</c> alone is not unique, and offset paging over ties is
/// nondeterministic. The rows cannot prove this. With the <c>(UserId, CreatedAt DESC, Id DESC)</c>
/// index from audit L12, Postgres may read the per-user list in index order and return ties Id-descending
/// even when the query asks only for <c>CreatedAt</c>. Removing the tie-break left the row-based test in
/// <c>GetOrdersByUserTests</c> green in Stage 13, so this one asserts the SQL.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ListOrderSqlTests : AuthenticatedIntegrationTestBase
{
    private static readonly Regex CreatedAtThenId =
        new(@"ORDER BY \w+\.""CreatedAt"" DESC, \w+\.""Id"" DESC", RegexOptions.Compiled);

    protected override async Task<OrderingApiFactory> CreateFactoryAsync()
        => await SqlCapturingOrderingApiFactory.CreateAsync();

    [TestCase("/api/v1/users/{0}/orders?pageSize=7")]
    [TestCase("/api/v1/orders?pageSize=7")]
    public async Task TheListQuery_SortsByCreatedAtThenId(string template)
    {
        var factory = (SqlCapturingOrderingApiFactory)Factory;
        factory.Commands.Clear();

        var response = await Client.GetAsync(string.Format(template, TestUserId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Commands.Should().Contain(sql => CreatedAtThenId.IsMatch(sql),
            "the list must order by CreatedAt and then Id, both descending");
    }
}
