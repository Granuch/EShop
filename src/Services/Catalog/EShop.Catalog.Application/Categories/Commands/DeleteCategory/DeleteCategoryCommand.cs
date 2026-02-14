using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

public record DeleteCategoryCommand() : IRequest<Result>
{
    public Guid Id { get; set; }
}