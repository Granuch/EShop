using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

public record GetCategoryByIdQuery : IRequest<Result<CategoryDto>>
{
    public Guid Id { get; init; }
}