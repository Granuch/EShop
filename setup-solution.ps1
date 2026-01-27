# ========================================
# E-Shop Microservices Solution Setup
# ========================================

# Create solution
dotnet new sln -n EShop

# ========================================
# 1. BUILDINGBLOCKS (Shared Libraries)
# ========================================


# BuildingBlocks.Domain
dotnet new classlib -n EShop.BuildingBlocks.Domain -o src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet sln add src/BuildingBlocks/EShop.BuildingBlocks.Domain

# BuildingBlocks.Application
dotnet new classlib -n EShop.BuildingBlocks.Application -o src/BuildingBlocks/EShop.BuildingBlocks.Application
dotnet sln add src/BuildingBlocks/EShop.BuildingBlocks.Application

# BuildingBlocks.Infrastructure
dotnet new classlib -n EShop.BuildingBlocks.Infrastructure -o src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure
dotnet sln add src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

# BuildingBlocks.Messaging
dotnet new classlib -n EShop.BuildingBlocks.Messaging -o src/BuildingBlocks/EShop.BuildingBlocks.Messaging
dotnet sln add src/BuildingBlocks/EShop.BuildingBlocks.Messaging

# ========================================
# 2. IDENTITY SERVICE
# ========================================


# Identity.Domain
dotnet new classlib -n EShop.Identity.Domain -o src/Services/Identity/EShop.Identity.Domain
dotnet sln add src/Services/Identity/EShop.Identity.Domain

# Identity.Application
dotnet new classlib -n EShop.Identity.Application -o src/Services/Identity/EShop.Identity.Application
dotnet sln add src/Services/Identity/EShop.Identity.Application

# Identity.Infrastructure
dotnet new classlib -n EShop.Identity.Infrastructure -o src/Services/Identity/EShop.Identity.Infrastructure
dotnet sln add src/Services/Identity/EShop.Identity.Infrastructure

# Identity.API
dotnet new webapi -n EShop.Identity.API -o src/Services/Identity/EShop.Identity.API
dotnet sln add src/Services/Identity/EShop.Identity.API

# ========================================
# 3. CATALOG SERVICE
# ========================================


# Catalog.Domain
dotnet new classlib -n EShop.Catalog.Domain -o src/Services/Catalog/EShop.Catalog.Domain
dotnet sln add src/Services/Catalog/EShop.Catalog.Domain

# Catalog.Application
dotnet new classlib -n EShop.Catalog.Application -o src/Services/Catalog/EShop.Catalog.Application
dotnet sln add src/Services/Catalog/EShop.Catalog.Application

# Catalog.Infrastructure
dotnet new classlib -n EShop.Catalog.Infrastructure -o src/Services/Catalog/EShop.Catalog.Infrastructure
dotnet sln add src/Services/Catalog/EShop.Catalog.Infrastructure

# Catalog.API
dotnet new webapi -n EShop.Catalog.API -o src/Services/Catalog/EShop.Catalog.API
dotnet sln add src/Services/Catalog/EShop.Catalog.API

# ========================================
# 4. BASKET SERVICE
# ========================================


# Basket.Domain
dotnet new classlib -n EShop.Basket.Domain -o src/Services/Basket/EShop.Basket.Domain
dotnet sln add src/Services/Basket/EShop.Basket.Domain

# Basket.Application
dotnet new classlib -n EShop.Basket.Application -o src/Services/Basket/EShop.Basket.Application
dotnet sln add src/Services/Basket/EShop.Basket.Application

# Basket.Infrastructure
dotnet new classlib -n EShop.Basket.Infrastructure -o src/Services/Basket/EShop.Basket.Infrastructure
dotnet sln add src/Services/Basket/EShop.Basket.Infrastructure

# Basket.API
dotnet new webapi -n EShop.Basket.API -o src/Services/Basket/EShop.Basket.API
dotnet sln add src/Services/Basket/EShop.Basket.API

# ========================================
# 5. ORDERING SERVICE
# ========================================


# Ordering.Domain
dotnet new classlib -n EShop.Ordering.Domain -o src/Services/Ordering/EShop.Ordering.Domain
dotnet sln add src/Services/Ordering/EShop.Ordering.Domain

# Ordering.Application
dotnet new classlib -n EShop.Ordering.Application -o src/Services/Ordering/EShop.Ordering.Application
dotnet sln add src/Services/Ordering/EShop.Ordering.Application

# Ordering.Infrastructure
dotnet new classlib -n EShop.Ordering.Infrastructure -o src/Services/Ordering/EShop.Ordering.Infrastructure
dotnet sln add src/Services/Ordering/EShop.Ordering.Infrastructure

# Ordering.API
dotnet new webapi -n EShop.Ordering.API -o src/Services/Ordering/EShop.Ordering.API
dotnet sln add src/Services/Ordering/EShop.Ordering.API

# ========================================
# 6. PAYMENT SERVICE
# ========================================


# Payment.Domain
dotnet new classlib -n EShop.Payment.Domain -o src/Services/Payment/EShop.Payment.Domain
dotnet sln add src/Services/Payment/EShop.Payment.Domain

# Payment.Application
dotnet new classlib -n EShop.Payment.Application -o src/Services/Payment/EShop.Payment.Application
dotnet sln add src/Services/Payment/EShop.Payment.Application

# Payment.Infrastructure
dotnet new classlib -n EShop.Payment.Infrastructure -o src/Services/Payment/EShop.Payment.Infrastructure
dotnet sln add src/Services/Payment/EShop.Payment.Infrastructure

# Payment.API
dotnet new webapi -n EShop.Payment.API -o src/Services/Payment/EShop.Payment.API
dotnet sln add src/Services/Payment/EShop.Payment.API

# ========================================
# 7. NOTIFICATION SERVICE
# ========================================


# Notification.Domain
dotnet new classlib -n EShop.Notification.Domain -o src/Services/Notification/EShop.Notification.Domain
dotnet sln add src/Services/Notification/EShop.Notification.Domain

# Notification.Application
dotnet new classlib -n EShop.Notification.Application -o src/Services/Notification/EShop.Notification.Application
dotnet sln add src/Services/Notification/EShop.Notification.Application

# Notification.Infrastructure
dotnet new classlib -n EShop.Notification.Infrastructure -o src/Services/Notification/EShop.Notification.Infrastructure
dotnet sln add src/Services/Notification/EShop.Notification.Infrastructure

# Notification.API (Worker Service)
dotnet new worker -n EShop.Notification.API -o src/Services/Notification/EShop.Notification.API
dotnet sln add src/Services/Notification/EShop.Notification.API

# ========================================
# 8. API GATEWAY
# ========================================


dotnet new webapi -n EShop.ApiGateway -o src/ApiGateways/EShop.ApiGateway
dotnet sln add src/ApiGateways/EShop.ApiGateway

# ========================================
# 9. TEST PROJECTS
# ========================================


# Identity Tests
dotnet new xunit -n EShop.Identity.UnitTests -o tests/Services/Identity/EShop.Identity.UnitTests
dotnet sln add tests/Services/Identity/EShop.Identity.UnitTests

dotnet new xunit -n EShop.Identity.IntegrationTests -o tests/Services/Identity/EShop.Identity.IntegrationTests
dotnet sln add tests/Services/Identity/EShop.Identity.IntegrationTests

# Catalog Tests
dotnet new xunit -n EShop.Catalog.UnitTests -o tests/Services/Catalog/EShop.Catalog.UnitTests
dotnet sln add tests/Services/Catalog/EShop.Catalog.UnitTests

dotnet new xunit -n EShop.Catalog.IntegrationTests -o tests/Services/Catalog/EShop.Catalog.IntegrationTests
dotnet sln add tests/Services/Catalog/EShop.Catalog.IntegrationTests

# ========================================
# 10. ADD PROJECT REFERENCES
# ========================================


# BuildingBlocks dependencies
dotnet add src/BuildingBlocks/EShop.BuildingBlocks.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Application

# Identity Service dependencies
dotnet add src/Services/Identity/EShop.Identity.Application reference src/Services/Identity/EShop.Identity.Domain
dotnet add src/Services/Identity/EShop.Identity.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Identity/EShop.Identity.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Identity/EShop.Identity.Infrastructure reference src/Services/Identity/EShop.Identity.Domain
dotnet add src/Services/Identity/EShop.Identity.Infrastructure reference src/Services/Identity/EShop.Identity.Application
dotnet add src/Services/Identity/EShop.Identity.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Identity/EShop.Identity.API reference src/Services/Identity/EShop.Identity.Application
dotnet add src/Services/Identity/EShop.Identity.API reference src/Services/Identity/EShop.Identity.Infrastructure

# Catalog Service dependencies
dotnet add src/Services/Catalog/EShop.Catalog.Application reference src/Services/Catalog/EShop.Catalog.Domain
dotnet add src/Services/Catalog/EShop.Catalog.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Catalog/EShop.Catalog.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Catalog/EShop.Catalog.Infrastructure reference src/Services/Catalog/EShop.Catalog.Domain
dotnet add src/Services/Catalog/EShop.Catalog.Infrastructure reference src/Services/Catalog/EShop.Catalog.Application
dotnet add src/Services/Catalog/EShop.Catalog.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Catalog/EShop.Catalog.API reference src/Services/Catalog/EShop.Catalog.Application
dotnet add src/Services/Catalog/EShop.Catalog.API reference src/Services/Catalog/EShop.Catalog.Infrastructure

# Basket Service dependencies (same pattern)
dotnet add src/Services/Basket/EShop.Basket.Application reference src/Services/Basket/EShop.Basket.Domain
dotnet add src/Services/Basket/EShop.Basket.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Basket/EShop.Basket.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Basket/EShop.Basket.Infrastructure reference src/Services/Basket/EShop.Basket.Domain
dotnet add src/Services/Basket/EShop.Basket.Infrastructure reference src/Services/Basket/EShop.Basket.Application
dotnet add src/Services/Basket/EShop.Basket.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Basket/EShop.Basket.API reference src/Services/Basket/EShop.Basket.Application
dotnet add src/Services/Basket/EShop.Basket.API reference src/Services/Basket/EShop.Basket.Infrastructure

# Ordering Service dependencies
dotnet add src/Services/Ordering/EShop.Ordering.Application reference src/Services/Ordering/EShop.Ordering.Domain
dotnet add src/Services/Ordering/EShop.Ordering.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Ordering/EShop.Ordering.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Ordering/EShop.Ordering.Infrastructure reference src/Services/Ordering/EShop.Ordering.Domain
dotnet add src/Services/Ordering/EShop.Ordering.Infrastructure reference src/Services/Ordering/EShop.Ordering.Application
dotnet add src/Services/Ordering/EShop.Ordering.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Ordering/EShop.Ordering.API reference src/Services/Ordering/EShop.Ordering.Application
dotnet add src/Services/Ordering/EShop.Ordering.API reference src/Services/Ordering/EShop.Ordering.Infrastructure

# Payment Service dependencies
dotnet add src/Services/Payment/EShop.Payment.Application reference src/Services/Payment/EShop.Payment.Domain
dotnet add src/Services/Payment/EShop.Payment.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Payment/EShop.Payment.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Payment/EShop.Payment.Infrastructure reference src/Services/Payment/EShop.Payment.Domain
dotnet add src/Services/Payment/EShop.Payment.Infrastructure reference src/Services/Payment/EShop.Payment.Application
dotnet add src/Services/Payment/EShop.Payment.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Payment/EShop.Payment.API reference src/Services/Payment/EShop.Payment.Application
dotnet add src/Services/Payment/EShop.Payment.API reference src/Services/Payment/EShop.Payment.Infrastructure

# Notification Service dependencies
dotnet add src/Services/Notification/EShop.Notification.Application reference src/Services/Notification/EShop.Notification.Domain
dotnet add src/Services/Notification/EShop.Notification.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Domain
dotnet add src/Services/Notification/EShop.Notification.Application reference src/BuildingBlocks/EShop.BuildingBlocks.Application

dotnet add src/Services/Notification/EShop.Notification.Infrastructure reference src/Services/Notification/EShop.Notification.Domain
dotnet add src/Services/Notification/EShop.Notification.Infrastructure reference src/Services/Notification/EShop.Notification.Application
dotnet add src/Services/Notification/EShop.Notification.Infrastructure reference src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure

dotnet add src/Services/Notification/EShop.Notification.API reference src/Services/Notification/EShop.Notification.Application
dotnet add src/Services/Notification/EShop.Notification.API reference src/Services/Notification/EShop.Notification.Infrastructure

# Test project references
dotnet add tests/Services/Identity/EShop.Identity.UnitTests reference src/Services/Identity/EShop.Identity.Domain
dotnet add tests/Services/Identity/EShop.Identity.UnitTests reference src/Services/Identity/EShop.Identity.Application

dotnet add tests/Services/Identity/EShop.Identity.IntegrationTests reference src/Services/Identity/EShop.Identity.API

dotnet add tests/Services/Catalog/EShop.Catalog.UnitTests reference src/Services/Catalog/EShop.Catalog.Domain
dotnet add tests/Services/Catalog/EShop.Catalog.UnitTests reference src/Services/Catalog/EShop.Catalog.Application

dotnet add tests/Services/Catalog/EShop.Catalog.IntegrationTests reference src/Services/Catalog/EShop.Catalog.API

