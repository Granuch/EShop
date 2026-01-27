# 📋 EShop Microservices - Class Structure

This document provides an overview of the class structure created for the EShop microservices project.

## 🏗️ Structure Created

All class files have been created with **TODO comments** indicating what needs to be implemented. This provides a clear roadmap for implementation while maintaining the correct architecture.

---

## 📦 BuildingBlocks

Shared libraries containing common patterns and infrastructure code.

### EShop.BuildingBlocks.Domain
- ✅ `Entity<TId>` - Base entity class with audit fields
- ✅ `AggregateRoot<TId>` - Base aggregate root with domain events
- ✅ `ValueObject` - Base value object with equality comparison
- ✅ `IDomainEvent` - Marker interface for domain events
- ✅ `IRepository<T, TId>` - Generic repository pattern
- ✅ `DomainException` - Domain-level exception

### EShop.BuildingBlocks.Application
- ✅ `Result<T>` - Result pattern for operation outcomes
- ✅ `Error` - Error representation
- ✅ `NotFoundException` - Entity not found exception
- ✅ `ValidationException` - Validation failure exception
- ✅ `LoggingBehavior<TRequest, TResponse>` - MediatR logging pipeline
- ✅ `ValidationBehavior<TRequest, TResponse>` - MediatR validation pipeline
- ✅ `PagedResult<T>` - Pagination wrapper

### EShop.BuildingBlocks.Infrastructure
- ✅ `BaseDbContext` - Base DbContext with domain event dispatching
- ✅ `EfRepository<T, TId>` - Generic EF Core repository
- ✅ `DistributedCacheExtensions` - Redis cache helpers

### EShop.BuildingBlocks.Messaging
- ✅ `IIntegrationEvent` - Base interface for integration events
- ✅ `IntegrationEvent` - Base class for integration events
- ✅ `BasketCheckedOutEvent` - Event when basket checkout occurs
- ✅ `OrderCreatedEvent` - Event when order is created
- ✅ `PaymentSuccessEvent` - Event when payment succeeds
- ✅ `PaymentFailedEvent` - Event when payment fails
- ✅ `OrderShippedEvent` - Event when order is shipped

---

## 🔐 Identity Service

### Domain Layer
- ✅ `ApplicationUser` - Extended IdentityUser with custom properties
- ✅ `ApplicationRole` - Extended IdentityRole
- ✅ `RefreshToken` - Value object for refresh tokens
- ✅ `EmailConfirmationToken` - Value object for email confirmation
- ✅ `UserRegisteredEvent` - Domain event
- ✅ `UserEmailConfirmedEvent` - Domain event
- ✅ `UserPasswordResetRequestedEvent` - Domain event
- ✅ `IUserRepository` - User repository interface
- ✅ `ITokenService` - JWT token service interface

### Application Layer
- ✅ `RegisterCommand` & `RegisterCommandHandler` - User registration
- ✅ `RegisterCommandValidator` - Registration validation
- ✅ `LoginCommand` & `LoginCommandHandler` - User login
- ✅ `RefreshTokenCommand` & `RefreshTokenCommandHandler` - Token refresh

### API Layer
- ✅ `AuthController` - Authentication endpoints
- ✅ `AccountController` - Account management endpoints

### Infrastructure Layer
- ✅ `IdentityDbContext` - EF Core DbContext for Identity

---

## 📦 Catalog Service

### Domain Layer
- ✅ `Product` - Product aggregate root with business logic
- ✅ `ProductImage` - Product image entity
- ✅ `ProductAttribute` - Product attributes (variants)
- ✅ `Category` - Category entity with hierarchy support
- ✅ `ProductCreatedEvent` - Domain event
- ✅ `ProductPriceChangedEvent` - Domain event
- ✅ `ProductOutOfStockEvent` - Domain event
- ✅ `IProductRepository` - Product repository interface

### Application Layer
- ✅ `CreateProductCommand` & `CreateProductCommandHandler` - Create product
- ✅ `CreateProductCommandValidator` - Product validation
- ✅ `GetProductsQuery` & `GetProductsQueryHandler` - Get products with filters
- ✅ `ProductDto` - Product data transfer object

### API Layer
- ✅ `ProductEndpoints` - Minimal API endpoints for products
- ✅ `CategoryEndpoints` - Minimal API endpoints for categories

### Infrastructure Layer
- ✅ `CatalogDbContext` - EF Core DbContext for Catalog

---

## 🛒 Basket Service

### Domain Layer
- ✅ `ShoppingBasket` - Basket aggregate root
- ✅ `BasketItem` - Basket item entity
- ✅ `IBasketRepository` - Basket repository interface (Redis)

### Application Layer
- ✅ `AddItemToBasketCommand` & `AddItemToBasketCommandHandler`
- ✅ `CheckoutBasketCommand` & `CheckoutBasketCommandHandler`
- ✅ `GetBasketQuery` - Get user's basket
- ✅ `BasketDto` - Basket data transfer object

### API Layer
- ✅ `BasketEndpoints` - Minimal API endpoints for basket

### Infrastructure Layer
- ✅ `RedisBasketRepository` - Redis-based basket storage

---

## 📋 Ordering Service

### Domain Layer
- ✅ `Order` - Order aggregate root with DDD patterns
- ✅ `OrderItem` - Order item entity
- ✅ `Address` - Address value object
- ✅ `OrderCreatedDomainEvent` - Domain event
- ✅ `OrderPaidDomainEvent` - Domain event
- ✅ `IOrderRepository` - Order repository interface

### Application Layer
- ✅ `CreateOrderCommand` & `CreateOrderCommandHandler`
- ✅ `BasketCheckedOutConsumer` - Consumes basket checkout events
- ✅ `PaymentSuccessConsumer` - Consumes payment success events

### API Layer
- ✅ `OrderEndpoints` - Minimal API endpoints for orders

### Infrastructure Layer
- ✅ `OrderingDbContext` - EF Core DbContext for Ordering

---

## 💳 Payment Service

### Domain Layer
- ✅ `PaymentTransaction` - Payment transaction entity
- ✅ `IPaymentProcessor` - Payment processor interface
- ✅ `PaymentResult` - Payment operation result

### Application Layer
- ✅ `OrderCreatedConsumer` - Processes payments when order is created

### Infrastructure Layer
- ✅ `MockPaymentProcessor` - Mock payment processor (80% success rate)

---

## 📧 Notification Service

### Domain Layer
- ✅ `NotificationLog` - Notification log entity
- ✅ `IEmailService` - Email service interface
- ✅ Email DTOs (`OrderConfirmationEmail`, `OrderShippedEmail`, etc.)

### Application Layer
- ✅ `OrderCreatedConsumer` - Sends order confirmation email
- ✅ `OrderShippedConsumer` - Sends shipping notification
- ✅ `PaymentFailedConsumer` - Sends payment failure notification

### Infrastructure Layer
- ✅ `EmailService` - Email service using MailKit

---

## 🚀 Next Steps

### 1. Implement TODO Items
Each class has TODO comments indicating what needs to be implemented:
- Domain logic in aggregate roots
- Command/query handlers
- Validators
- Repository implementations
- API endpoint implementations
- MassTransit consumer logic

### 2. Add NuGet Packages
Install required packages for each project:
```bash
# Example for Identity.Domain
cd src/Services/Identity/EShop.Identity.Domain
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore

# Example for BuildingBlocks.Application
cd src/BuildingBlocks/EShop.BuildingBlocks.Application
dotnet add package MediatR
dotnet add package FluentValidation
```

### 3. Configure Dependency Injection
- Create `ServiceCollectionExtensions.cs` in each Infrastructure project
- Register DbContexts, repositories, services, MediatR, FluentValidation
- Configure MassTransit with RabbitMQ

### 4. Create Migrations
```bash
# Identity Service
dotnet ef migrations add InitialCreate --project src/Services/Identity/EShop.Identity.Infrastructure

# Catalog Service
dotnet ef migrations add InitialCreate --project src/Services/Catalog/EShop.Catalog.Infrastructure

# Ordering Service
dotnet ef migrations add InitialCreate --project src/Services/Ordering/EShop.Ordering.Infrastructure
```

### 5. Configure Program.cs
- Add authentication/authorization
- Configure MassTransit
- Add Serilog logging
- Configure OpenTelemetry
- Add health checks
- Configure output caching

### 6. Create Docker Compose
Set up infrastructure services:
- PostgreSQL databases
- Redis
- RabbitMQ
- Seq (logging)
- Jaeger (tracing)

### 7. Implement Tests
Create unit and integration tests for:
- Domain logic (aggregate roots)
- Command/query handlers
- API endpoints
- Consumers

---

## 📚 Architecture Patterns Used

- ✅ **Clean Architecture** - Separation of concerns with layers
- ✅ **Domain-Driven Design (DDD)** - Aggregates, value objects, domain events
- ✅ **CQRS** - Command/Query separation with MediatR
- ✅ **Repository Pattern** - Data access abstraction
- ✅ **Result Pattern** - Functional error handling
- ✅ **Event-Driven Architecture** - Integration events with RabbitMQ/MassTransit
- ✅ **Decorator Pattern** - Caching, logging behaviors

---

## 📖 Documentation Reference

All implementation details are based on documentation in `docs/` folder:
- `docs/04-services/` - Detailed service specifications
- `docs/03-architecture/` - Architecture decisions and patterns
- `docs/03-implementation-plan/` - Phase-by-phase implementation guide

---

**Version**: 1.0  
**Last Updated**: 2024-01-15  
**Status**: Structure Created - Ready for Implementation
