using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public record CreateCategoryCommand : IRequest<Result>
{
    public string Name { get; set; } = string.Empty;
        
    public string? Slug { get; set; }

    public Guid? ParentCategoryId { get; set; }
}