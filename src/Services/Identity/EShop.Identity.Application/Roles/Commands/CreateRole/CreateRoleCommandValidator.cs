using FluentValidation;

namespace EShop.Identity.Application.Roles.Commands.CreateRole;

/// <summary>
/// The role endpoints had no validation at all before this — a create with an empty name reached
/// RoleManager and came back as a 400 carrying an ASP.NET Identity string. Rules here produce the
/// canonical Validation.Failed envelope instead.
/// </summary>
public class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required")
            .MaximumLength(256).WithMessage("Role name must not exceed 256 characters")
            .Matches("^[A-Za-z0-9 _-]+$")
            .WithMessage("Role name may contain only letters, digits, spaces, underscores and hyphens");

        // Matches ApplicationRole.Description's HasMaxLength(250) mapping; without this a longer
        // description reaches the database and fails as a persistence error rather than a 400.
        RuleFor(x => x.Description)
            .MaximumLength(250).WithMessage("Description must not exceed 250 characters")
            .When(x => x.Description is not null);
    }
}
