using System.Net;
using System.Text;
using System.Text.Json;

namespace EShop.Payment.IntegrationTests.Errors;

/// <summary>
/// Frontend-contracts F-20. Payment set neither <c>ThrowOnBadRequest</c> nor <c>AddMalformedJsonBody</c>, so a body that
/// is not JSON, a missing body and an unbindable query value were all a bare 400 with an empty body and no errorCode.
/// </summary>
[TestFixture]
[Category("Integration")]
public class MalformedRequestTests : AuthenticatedIntegrationTestBase
{
    [TestCase("{\"orderId\": ")]
    [TestCase("not json")]
    public async Task AMalformedBody_IsAProblemJson400(string body)
    {
        using var response = await Client.PostAsync(
            "/api/v1/payments/create-intent", new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertMalformedRequestAsync(response);
    }

    [Test]
    public async Task AMissingBody_IsAProblemJson400()
    {
        using var response = await Client.PostAsync("/api/v1/payments/create-intent", content: null);

        await AssertMalformedRequestAsync(response);
    }

    [TestCase("pageSize=abc")]
    [TestCase("pageNumber=abc")]
    public async Task AnUnbindableQueryValue_IsAProblemJson400(string query)
    {
        using var response = await Client.GetAsync($"/api/v1/users/{TestUserId}/payments?{query}");

        await AssertMalformedRequestAsync(response);
    }

    private static async Task AssertMalformedRequestAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("errorCode").GetString(), Is.EqualTo("MalformedRequest"));
        Assert.That(json.RootElement.GetProperty("traceId").GetString(), Is.Not.Empty);
    }
}
