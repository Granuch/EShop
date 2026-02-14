using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public record UpdateCategoryCommand : IRequest<Result>
{
    public Guid Id { get; set; }
    public string Name { get; init; }
    public string Description { get; init; }
}