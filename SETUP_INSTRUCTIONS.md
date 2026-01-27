# 🔧 Setup Instructions for EShop Microservices

This guide will help you set up all required dependencies and fix compilation errors.

## 📦 Step 1: Add Project References

### BuildingBlocks.Domain
No external dependencies needed - only .NET 10 SDK.

### BuildingBlocks.Application
```bash
cd src/BuildingBlocks/EShop.BuildingBlocks.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ../EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

### BuildingBlocks.Infrastructure
```bash
cd src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore --version 9.0.0
dotnet add package Microsoft.Extensions.Caching.Abstractions --version 9.0.0
dotnet add reference ../EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

### BuildingBlocks.Messaging
```bash
cd src/BuildingBlocks/EShop.BuildingBlocks.Messaging
dotnet add package MediatR --version 12.4.1
dotnet add reference ../EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

---

## 🔐 Identity Service

### Identity.Domain
```bash
cd src/Services/Identity/EShop.Identity.Domain
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

### Identity.Application
```bash
cd src/Services/Identity/EShop.Identity.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj
dotnet add reference ../EShop.Identity.Domain/EShop.Identity.Domain.csproj
```

### Identity.Infrastructure
```bash
cd src/Services/Identity/EShop.Identity.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ../EShop.Identity.Domain/EShop.Identity.Domain.csproj
```

### Identity.API
```bash
cd src/Services/Identity/EShop.Identity.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ../EShop.Identity.Application/EShop.Identity.Application.csproj
dotnet add reference ../EShop.Identity.Infrastructure/EShop.Identity.Infrastructure.csproj
```

---

## 📦 Catalog Service

### Catalog.Domain
```bash
cd src/Services/Catalog/EShop.Catalog.Domain
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

### Catalog.Application
```bash
cd src/Services/Catalog/EShop.Catalog.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj
dotnet add reference ../EShop.Catalog.Domain/EShop.Catalog.Domain.csproj
```

### Catalog.Infrastructure
```bash
cd src/Services/Catalog/EShop.Catalog.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 9.0.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ../EShop.Catalog.Domain/EShop.Catalog.Domain.csproj
```

### Catalog.API
```bash
cd src/Services/Catalog/EShop.Catalog.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ../EShop.Catalog.Application/EShop.Catalog.Application.csproj
dotnet add reference ../EShop.Catalog.Infrastructure/EShop.Catalog.Infrastructure.csproj
```

---

## 🛒 Basket Service

### Basket.Domain
```bash
cd src/Services/Basket/EShop.Basket.Domain
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj
```

### Basket.Application
```bash
cd src/Services/Basket/EShop.Basket.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj
dotnet add reference ../EShop.Basket.Domain/EShop.Basket.Domain.csproj
```

### Basket.Infrastructure
```bash
cd src/Services/Basket/EShop.Basket.Infrastructure
dotnet add package StackExchange.Redis --version 2.8.16
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ../EShop.Basket.Domain/EShop.Basket.Domain.csproj
```

### Basket.API
```bash
cd src/Services/Basket/EShop.Basket.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ../EShop.Basket.Application/EShop.Basket.Application.csproj
dotnet add reference ../EShop.Basket.Infrastructure/EShop.Basket.Infrastructure.csproj
```

---

## 📋 Ordering Service

### Ordering.Domain
```bash
cd src/Services/Ordering/EShop.Ordering.Domain
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Domain/EShop.BuildingBlocks.Domain.csproj
```

### Ordering.Application
```bash
cd src/Services/Ordering/EShop.Ordering.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add package MassTransit --version 8.3.3
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Application/EShop.BuildingBlocks.Application.csproj
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ../EShop.Ordering.Domain/EShop.Ordering.Domain.csproj
```

### Ordering.Infrastructure
```bash
cd src/Services/Ordering/EShop.Ordering.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Infrastructure/EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ../EShop.Ordering.Domain/EShop.Ordering.Domain.csproj
```

### Ordering.API
```bash
cd src/Services/Ordering/EShop.Ordering.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ../EShop.Ordering.Application/EShop.Ordering.Application.csproj
dotnet add reference ../EShop.Ordering.Infrastructure/EShop.Ordering.Infrastructure.csproj
```

---

## 💳 Payment Service

### Payment.Domain
```bash
cd src/Services/Payment/EShop.Payment.Domain
# No external dependencies
```

### Payment.Application
```bash
cd src/Services/Payment/EShop.Payment.Application
dotnet add package MassTransit --version 8.3.3
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ../EShop.Payment.Domain/EShop.Payment.Domain.csproj
```

### Payment.Infrastructure
```bash
cd src/Services/Payment/EShop.Payment.Infrastructure
dotnet add reference ../EShop.Payment.Domain/EShop.Payment.Domain.csproj
```

### Payment.API
```bash
cd src/Services/Payment/EShop.Payment.API
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ../EShop.Payment.Application/EShop.Payment.Application.csproj
dotnet add reference ../EShop.Payment.Infrastructure/EShop.Payment.Infrastructure.csproj
```

---

## 📧 Notification Service

### Notification.Domain
```bash
cd src/Services/Notification/EShop.Notification.Domain
# No external dependencies
```

### Notification.Application
```bash
cd src/Services/Notification/EShop.Notification.Application
dotnet add package MassTransit --version 8.3.3
dotnet add reference ../../../BuildingBlocks/EShop.BuildingBlocks.Messaging/EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ../EShop.Notification.Domain/EShop.Notification.Domain.csproj
```

### Notification.Infrastructure
```bash
cd src/Services/Notification/EShop.Notification.Infrastructure
dotnet add package MailKit --version 4.8.0
dotnet add reference ../EShop.Notification.Domain/EShop.Notification.Domain.csproj
```

### Notification.API
```bash
cd src/Services/Notification/EShop.Notification.API
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ../EShop.Notification.Application/EShop.Notification.Application.csproj
dotnet add reference ../EShop.Notification.Infrastructure/EShop.Notification.Infrastructure.csproj
```

---

## 🚪 API Gateway

```bash
cd src/ApiGateways/EShop.ApiGateway
dotnet add package Yarp.ReverseProxy --version 2.3.0
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
```

---

## ✅ Step 2: Verify Build

After adding all dependencies, rebuild the solution:

```bash
dotnet restore
dotnet build
```

If there are still errors, they will be related to missing project references. Add them as needed.

---

## 🐳 Step 3: Setup Infrastructure with Docker Compose

Create `docker-compose.yml` in the root folder:

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

  rabbitmq:
    image: rabbitmq:3.13-management-alpine
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest

  seq:
    image: datalust/seq:latest
    environment:
      ACCEPT_EULA: Y
    ports:
      - "5341:80"
    volumes:
      - seq_data:/data

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "6831:6831/udp"
      - "16686:16686"

volumes:
  postgres_data:
  redis_data:
  seq_data:
```

Start infrastructure:
```bash
docker-compose up -d
```

---

## 📝 Step 4: Next Steps

1. **Implement TODO items** in each class
2. **Configure appsettings.json** for each service
3. **Create EF Core migrations**
4. **Configure dependency injection** in Program.cs
5. **Write unit and integration tests**

See `CLASS_STRUCTURE.md` for detailed overview of all created classes.

---

**Good luck with implementation! 🚀**
