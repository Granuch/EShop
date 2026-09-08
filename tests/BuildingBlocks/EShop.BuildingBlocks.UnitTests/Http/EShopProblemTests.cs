using System.Text.Json;
using EShop.BuildingBlocks.Infrastructure.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>
/// TEST-03. Pins the error envelope every service emits, plus the serialization traps that make
/// it easy to break without any compiler complaint.
/// </summary>
[TestFixture]
public class EShopProblemTests
{
    private static HttpContext ContextWith(string traceId)
        => new DefaultHttpContext { TraceIdentifier = traceId };

    [Test]
    public void Create_AttachesErrorCodeAndTraceId()
    {
        var problem = EShopProblem.Create(ContextWith("trace-1"), 404, "not found", "Product.NotFound");

        problem.Status.Should().Be(404);
        problem.Detail.Should().Be("not found");
        problem.Extensions[EShopProblem.ErrorCodeKey].Should().Be("Product.NotFound");
        problem.Extensions[EShopProblem.TraceIdKey].Should().Be("trace-1");
    }

    /// <summary>
    /// <c>Type</c> and <c>Title</c> must stay null here. They are filled by the framework's own
    /// <c>ProblemDetailsDefaults</c> when the result is written; setting them here would create a
    /// second table of RFC 9110 URIs that can drift from the runtime on a .NET upgrade, and drift
    /// would split the contract in two because the minimal-API path fills them regardless.
    /// </summary>
    [Test]
    public void Create_LeavesTypeAndTitleToTheFramework()
    {
        var problem = EShopProblem.Create(ContextWith("trace-2"), 400, "bad", "Validation.Failed");

        problem.Type.Should().BeNull();
        problem.Title.Should().BeNull();
    }

    [Test]
    public void Create_OmitsErrorCode_WhenNoneIsGiven()
    {
        var problem = EShopProblem.Create(ContextWith("trace-3"), 500);

        problem.Extensions.Should().NotContainKey(EShopProblem.ErrorCodeKey);
        problem.Extensions[EShopProblem.TraceIdKey].Should().Be("trace-3");
    }

    /// <summary>
    /// Validation errors ride in <c>Extensions["errors"]</c> on a plain ProblemDetails rather than
    /// on a <c>ValidationProblemDetails</c>. That is not a style choice: serializing a
    /// ValidationProblemDetails through a ProblemDetails-typed reference silently drops its
    /// <c>Errors</c> property, so the client would receive a 400 with no field information and
    /// nothing anywhere would fail.
    /// </summary>
    [Test]
    public void Create_CarriesValidationErrorsAsAnExtensionMember()
    {
        var errors = new Dictionary<string, string[]> { ["Email"] = ["Email is required"] };

        var problem = EShopProblem.Create(ContextWith("trace-4"), 400, "invalid", "Validation.Failed", errors);

        problem.Extensions.Should().ContainKey(EShopProblem.ErrorsKey);
        problem.Extensions[EShopProblem.ErrorsKey].Should().BeSameAs(errors);
    }

    [Test]
    public void Create_OmitsErrors_WhenTheDictionaryIsEmpty()
    {
        var problem = EShopProblem.Create(
            ContextWith("trace-5"), 400, "invalid", "Validation.Failed", new Dictionary<string, string[]>());

        problem.Extensions.Should().NotContainKey(EShopProblem.ErrorsKey);
    }

    /// <summary>
    /// Extension keys bypass <c>PropertyNamingPolicy</c> — <c>[JsonExtensionData]</c> writes them
    /// verbatim — so they must already be camelCase in the constants. Renaming a constant to
    /// PascalCase would change the wire contract for every service at once, silently.
    /// </summary>
    [Test]
    public void ExtensionKeysAreSerializedVerbatim_SoTheConstantsMustAlreadyBeCamelCase()
    {
        EShopProblem.ErrorCodeKey.Should().Be("errorCode");
        EShopProblem.TraceIdKey.Should().Be("traceId");
        EShopProblem.ErrorsKey.Should().Be("errors");

        var problem = EShopProblem.Create(ContextWith("trace-6"), 409, "conflict", "Order.Conflict");

        var json = JsonSerializer.Serialize(
            problem,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("errorCode", out var errorCode).Should().BeTrue();
        errorCode.GetString().Should().Be("Order.Conflict");
        document.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }
}
