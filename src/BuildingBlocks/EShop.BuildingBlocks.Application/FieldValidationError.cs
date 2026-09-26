using System.Text.Json;
using FluentValidation.Results;

namespace EShop.BuildingBlocks.Application;

/// <summary>
/// A request that failed validation, with its messages per field. Every validation failure in EShop answers with this
/// one shape, whichever path produced it (frontend-contracts F-03): 400, <c>errorCode</c> <see cref="ErrorCode"/>, and an
/// <c>errors</c> map keyed by the <b>camelCase</b> wire name of the field. A rule about the request as a whole, rather
/// than one field, is keyed <see cref="RequestKey"/>.
/// </summary>
public sealed record FieldValidationError : Error
{
    public const string ErrorCode = "ValidationError";

    /// <summary>The key for a rule about the whole request (a cross-field rule such as "from before to").</summary>
    public const string RequestKey = "$";

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public FieldValidationError(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCode, DetailFor(errors))
    {
        Errors = errors;
    }

    public static FieldValidationError For(string field, string message)
        => new(new Dictionary<string, string[]> { [KeyFor(field)] = [message] });

    public static FieldValidationError From(IEnumerable<ValidationFailure> failures)
        => new(ToErrorMap(failures));

    public static Dictionary<string, string[]> ToErrorMap(IEnumerable<ValidationFailure> failures)
        => failures
            .GroupBy(f => KeyFor(f.PropertyName), f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());

    /// <summary>
    /// The wire name of a property path: each segment camelCased (<c>Items[0].Quantity</c> → <c>items[0].quantity</c>),
    /// and an empty path — FluentValidation's name for a rule on the whole object — becomes <see cref="RequestKey"/>.
    /// </summary>
    public static string KeyFor(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return RequestKey;
        }

        // The serializer's own policy, per segment, so a key always equals the property's JSON name.
        return string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }

    /// <summary>Every message once, in order, for clients that show one form-level line.</summary>
    public static string DetailFor(IReadOnlyDictionary<string, string[]> errors)
        => string.Join("; ", errors.Values.SelectMany(m => m).Distinct());
}
