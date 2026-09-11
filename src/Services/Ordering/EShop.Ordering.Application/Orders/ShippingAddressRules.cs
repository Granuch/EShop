using EShop.Ordering.Domain.ValueObjects;
using FluentValidation;

namespace EShop.Ordering.Application.Orders;

/// <summary>
/// Audit M1. Both create validators used to check only that Street, City and Country were non-empty,
/// so "USA", a missing state or a non-numeric US zip passed validation and then made
/// <see cref="Address"/> throw inside the handler. This reports <see cref="Address.Validate"/> itself,
/// field by field, so the two cannot drift: there is no second copy of the rules to keep in step.
/// </summary>
internal static class ShippingAddressRules
{
    public static void Check<T>(
        ValidationContext<T> context, string street, string city, string state, string zipCode, string country)
    {
        foreach (var problem in Address.Validate(street, city, state, zipCode, country))
        {
            context.AddFailure(problem.Field, problem.Message);
        }
    }
}
