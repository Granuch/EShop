using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Handler for creating a product
/// </summary>
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    // TODO: Inject IProductRepository, ILogger
    // private readonly IProductRepository _productRepository;

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        // TODO: Check if SKU already exists
        // TODO: Validate category exists
        // TODO: Create Product entity using factory method
        // TODO: Add to repository
        // TODO: Save changes
        // TODO: Publish ProductCreatedEvent
        // TODO: Return product ID
        throw new NotImplementedException();
    }
}
