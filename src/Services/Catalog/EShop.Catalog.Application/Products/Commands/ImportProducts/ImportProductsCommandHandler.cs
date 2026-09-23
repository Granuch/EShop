using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Application.Products.Commands.CreateProduct;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.ImportProducts;

/// <summary>
/// Checks every row, creates the ones that pass, saves once. See <see cref="ImportProductsCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal is decided before anything is added.</b> <c>TransactionBehavior</c> commits whatever the context
/// holds when the handler returns, so a refused row must never reach <see cref="IProductRepository.AddAsync"/>; the order
/// below — validate, then in-file duplicates, then the database's SKUs, then categories, then construct — keeps every
/// check ahead of the one line that adds.
/// </para>
/// <para>
/// The two database checks are one query each for the whole import, not one per row: a thousand-row import is two
/// round trips of checking and one save.
/// </para>
/// </remarks>
public class ImportProductsCommandHandler : IRequestHandler<ImportProductsCommand, Result<ProductImportReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateProductCommand> _rowValidator;

    public ImportProductsCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork,
        IValidator<CreateProductCommand> rowValidator)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _rowValidator = rowValidator;
    }

    public async Task<Result<ProductImportReport>> Handle(ImportProductsCommand request, CancellationToken cancellationToken)
    {
        var rows = request.Products!;
        var results = new ProductImportRowResult?[rows.Count];

        // 1. Each row against the create endpoint's own validator. Reported per row, rather than refusing the file, so
        //    one bad row does not hold back a thousand good ones.
        var commands = new CreateProductCommand?[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var command = rows[index].ToCreateCommand();
            var validation = await _rowValidator.ValidateAsync(command, cancellationToken);

            if (validation.IsValid)
            {
                commands[index] = command;
            }
            else
            {
                results[index] = Refused(index, rows[index].Sku, "Validation.Failed",
                    string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }
        }

        // 2. A SKU on more than one row refuses EVERY row carrying it — including a row already refused above, which
        //    keeps its first reason. Taking the first occurrence would be an arbitrary pick, and the admin who fixes the
        //    file and sends it again would then collide with whichever row this import happened to create.
        var rowsBySku = Enumerable.Range(0, rows.Count)
            .Where(i => !string.IsNullOrWhiteSpace(rows[i].Sku))
            .GroupBy(i => rows[i].Sku.Trim(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in rowsBySku)
        {
            var rowList = string.Join(", ", group);
            foreach (var index in group.Where(i => results[i] is null))
            {
                results[index] = Refused(index, rows[index].Sku, "Product.SkuConflict",
                    $"SKU '{group.Key}' appears on more than one row of this import (rows {rowList}).");
            }
        }

        var candidates = Enumerable.Range(0, rows.Count).Where(i => results[i] is null).ToList();

        // 3 and 4. The database's answers, one query each. SKU first, like CreateProductCommandHandler.
        var takenSkus = await _productRepository.GetTakenSkusAsync(
            candidates.Select(i => commands[i]!.Sku).Distinct(StringComparer.Ordinal).ToList(),
            cancellationToken);
        var existingCategories = await _categoryRepository.GetExistingIdsAsync(
            candidates.Select(i => commands[i]!.CategoryId).Distinct().ToList(),
            cancellationToken);

        foreach (var index in candidates)
        {
            var command = commands[index]!;

            if (takenSkus.Contains(command.Sku))
            {
                results[index] = Refused(index, command.Sku, "Product.SkuConflict",
                    $"Product with SKU '{command.Sku}' already exists.");
                continue;
            }

            if (!existingCategories.Contains(command.CategoryId))
            {
                results[index] = Refused(index, command.Sku, "Category.NotFound",
                    $"Category with ID '{command.CategoryId}' was not found.");
                continue;
            }

            Product product;
            try
            {
                // A factory: when it throws, nothing exists to be added, so catching here leaves nothing behind.
                product = Product.Create(
                    command.Name, command.Sku, command.Price, command.StockQuantity, command.CategoryId, command.Description);
            }
            catch (DomainException ex)
            {
                results[index] = Refused(index, command.Sku, BulkProductProcessor.DomainErrorCode, ex.Message);
                continue;
            }

            // The only line that adds. Everything above it can refuse; nothing below it can.
            await _productRepository.AddAsync(product, cancellationToken);
            results[index] = new ProductImportRowResult(index, command.Sku, product.Id, Succeeded: true);
        }

        // One save for the whole import. A unique violation here — a SKU taken since step 3 — is not caught: it fails
        // the request and rolls every row back (AddProductSkuConflict answers 409), which is what keeps the report honest.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var report = results.Select(r => r!).ToList();
        var created = report.Count(r => r.Succeeded);
        return Result<ProductImportReport>.Success(
            new ProductImportReport(report.Count, created, report.Count - created, report));
    }

    private static ProductImportRowResult Refused(int index, string? sku, string errorCode, string error)
        => new(index, sku, ProductId: null, Succeeded: false, errorCode, error);
}
