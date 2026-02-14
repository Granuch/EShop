using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Entities;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

public record GetCategoriesQuery : IRequest<Result<List<CategoryDto>>>;