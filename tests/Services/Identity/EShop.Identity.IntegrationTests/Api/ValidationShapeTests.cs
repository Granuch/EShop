using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Api;

/// <summary>
/// Frontend-contracts F-03. Identity had two validation shapes of its own: its controllers' Result path
/// (<c>Validation.Failed</c>, messages joined into <c>detail</c>) and MVC's automatic 400 (message in <c>title</c>, no
/// <c>detail</c>, keys as bound). Both now answer the one shape every service uses.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ValidationShapeTests : IntegrationTestBase
{
    private const string Register = "/api/v1/auth/register";

    [Test]
    public async Task AFluentValidationFailure_HasTheFieldMap()
    {
        using var response = await Client.PostAsJsonAsync(Register, new
        {
            email = "not-an-email",
            password = "Valid#Pass12345",
            firstName = "Shape",
            lastName = "Test"
        });

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
    }

    [Test]
    public async Task ABodyThatIsNotJson_IsTheSameShape()
    {
        using var response = await Client.PostAsync(Register, new StringContent("{", Encoding.UTF8, "application/json"));

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").TryGetProperty("$", out _).Should().BeTrue("a JSON syntax error is about the whole body");
        problem.GetProperty("type").GetString().Should().NotBeNullOrEmpty("MVC's factory still fills type and title");
    }

    [Test]
    public async Task ANullForARequiredString_IsKeyedByTheCamelCaseName()
    {
        using var response = await Client.PostAsJsonAsync(Register, new
        {
            email = (string?)null,
            password = "Valid#Pass12345",
            firstName = "Shape",
            lastName = "Test"
        });

        var problem = await AssertValidationShapeAsync(response);
        problem.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue(problem.ToString());
    }

    private static async Task<JsonElement> AssertValidationShapeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement.Clone();
        root.GetProperty("errorCode").GetString().Should().Be("ValidationError", body);
        root.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace(body);
        root.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object, body);
        foreach (var key in root.GetProperty("errors").EnumerateObject().Select(p => p.Name))
        {
            (key.StartsWith('$') || char.IsLower(key[0])).Should().BeTrue($"'{key}' must be a camelCase wire name or '$'");
        }

        return root;
    }
}
