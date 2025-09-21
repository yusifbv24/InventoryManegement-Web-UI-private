﻿using FluentValidation;
using MediatR;
using ProductService.Application.DTOs;
using ProductService.Application.Events;
using ProductService.Application.Interfaces;
using SharedServices.Exceptions;
using ProductService.Domain.Repositories;

namespace ProductService.Application.Features.Products.Commands
{
    public class UpdateProduct
    {
        public record Command(int Id, UpdateProductDto ProductDto,List<string> changes) : IRequest;

        public class Validator : AbstractValidator<Command>
        {
            public Validator()
            {
                RuleFor(x => x.ProductDto.CategoryId)
                    .GreaterThan(0).WithMessage("Valid category is required");

                RuleFor(x => x.ProductDto.DepartmentId)
                    .GreaterThan(0).WithMessage("Valid department is required");

                RuleFor(x => x.ProductDto.Description)
                    .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");
            }
        }

        public class UpdateProductCommandHandler : IRequestHandler<Command>
        {
            private readonly IProductRepository _productRepository;
            private readonly IUnitOfWork _unitOfWork;
            private readonly IImageService _imageService;
            private readonly ITransactionService _transactionService;
            private readonly IMessagePublisher _messagePublisher;

            public UpdateProductCommandHandler(
                IProductRepository productRepository,
                IUnitOfWork unitOfWork,
                IImageService imageService,
                ITransactionService transactionService,
                IMessagePublisher messagePublisher)
            {
                _productRepository = productRepository;
                _unitOfWork = unitOfWork;
                _imageService = imageService;
                _transactionService = transactionService;
                _messagePublisher = messagePublisher;
            }

            public async Task Handle(Command request, CancellationToken cancellationToken)
            {
                var product = await _productRepository.GetByIdAsync(request.Id, cancellationToken) ??
                    throw new NotFoundException($"Product with ID {request.Id} not found");

                var updatedProduct = request.ProductDto;
                var changes=request.changes;
                var existingProduct = new ProductDto
                {
                    Id = product.Id,
                    InventoryCode = product.InventoryCode,
                    Vendor = product.Vendor,
                    Model = product.Model,
                    CategoryId = product.CategoryId,
                    CategoryName = product.Category?.Name,
                    DepartmentId = product.DepartmentId,
                    DepartmentName = product.Department?.Name,
                    Worker = product.Worker,
                    Description = product.Description,
                    IsActive = product.IsActive,
                    IsNewItem = product.IsNewItem,
                    IsWorking = product.IsWorking,
                    ImageUrl = product.ImageUrl
                };

                var oldImageUrl = existingProduct.ImageUrl;
                var newImageUrl = oldImageUrl;
                var shouldUpdateImage = updatedProduct.ImageFile != null && updatedProduct.ImageFile.Length > 0;

                await _transactionService.ExecuteAsync(
                    async () =>
                    {
                        var updateEvent = new ProductUpdatedEvent
                        {
                            Product = existingProduct,
                            Changes = string.Join(", ", changes),
                            UpdatedAt = DateTime.Now,
                        };

                        if (shouldUpdateImage)
                        {
                            // Upload new image
                            using var stream = updatedProduct.ImageFile!.OpenReadStream();
                            newImageUrl = await _imageService.UploadImageAsync(stream, updatedProduct.ImageFile.FileName, product.InventoryCode);

                            using var ms = new MemoryStream();
                            await updatedProduct.ImageFile!.CopyToAsync(ms);
                            updateEvent.ImageData = ms.ToArray();
                            updateEvent.ImageFileName = updatedProduct.ImageFile.FileName;
                        }
                        // Delete old image only after successful update
                        if (shouldUpdateImage && !string.IsNullOrEmpty(oldImageUrl))
                        {
                            await _imageService.DeleteImageAsync(oldImageUrl);
                        }

                        // Update product with new image URL or keep the old one
                        product.Update(
                            updatedProduct.Model,
                            updatedProduct.Vendor,
                            updatedProduct.CategoryId,
                            updatedProduct.DepartmentId,
                            updatedProduct.Worker,
                            shouldUpdateImage ? newImageUrl : oldImageUrl,
                            updatedProduct.Description,
                            updatedProduct.IsActive,
                            updatedProduct.IsNewItem,
                            updatedProduct.IsWorking);

                        await _productRepository.UpdateAsync(product, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        await _messagePublisher.PublishAsync(updateEvent, "product.updated", cancellationToken);
                        return Task.CompletedTask;
                    },
                    async () =>
                    {
                        // Delete new image if update fails
                        if (!string.IsNullOrEmpty(newImageUrl))
                        {
                            await _imageService.DeleteImageAsync(newImageUrl);
                        }
                    });
            }
        }
    }
}