@echo off
echo Installing NuGet packages for EShop Microservices...

REM BuildingBlocks
echo BuildingBlocks.Application
cd src\BuildingBlocks\EShop.BuildingBlocks.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ..\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

echo BuildingBlocks.Infrastructure
cd ..\EShop.BuildingBlocks.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore --version 9.0.0
dotnet add package Microsoft.Extensions.Caching.Abstractions --version 9.0.0
dotnet add reference ..\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

echo BuildingBlocks.Messaging
cd ..\EShop.BuildingBlocks.Messaging
dotnet add package MediatR --version 12.4.1
dotnet add reference ..\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

REM Identity Service
echo Identity Service
cd ..\..\Services\Identity\EShop.Identity.Domain
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

cd ..\EShop.Identity.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Application\EShop.BuildingBlocks.Application.csproj
dotnet add reference ..\EShop.Identity.Domain\EShop.Identity.Domain.csproj

cd ..\EShop.Identity.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Infrastructure\EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ..\EShop.Identity.Domain\EShop.Identity.Domain.csproj

cd ..\EShop.Identity.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ..\EShop.Identity.Application\EShop.Identity.Application.csproj
dotnet add reference ..\EShop.Identity.Infrastructure\EShop.Identity.Infrastructure.csproj

REM Catalog Service
echo Catalog Service
cd ..\..\Catalog\EShop.Catalog.Domain
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

cd ..\EShop.Catalog.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Application\EShop.BuildingBlocks.Application.csproj
dotnet add reference ..\EShop.Catalog.Domain\EShop.Catalog.Domain.csproj

cd ..\EShop.Catalog.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 9.0.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Infrastructure\EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ..\EShop.Catalog.Domain\EShop.Catalog.Domain.csproj

cd ..\EShop.Catalog.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ..\EShop.Catalog.Application\EShop.Catalog.Application.csproj
dotnet add reference ..\EShop.Catalog.Infrastructure\EShop.Catalog.Infrastructure.csproj

REM Basket Service
echo Basket Service
cd ..\..\Basket\EShop.Basket.Domain
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Messaging\EShop.BuildingBlocks.Messaging.csproj

cd ..\EShop.Basket.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Application\EShop.BuildingBlocks.Application.csproj
dotnet add reference ..\EShop.Basket.Domain\EShop.Basket.Domain.csproj

cd ..\EShop.Basket.Infrastructure
dotnet add package StackExchange.Redis --version 2.8.16
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Infrastructure\EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ..\EShop.Basket.Domain\EShop.Basket.Domain.csproj

cd ..\EShop.Basket.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add reference ..\EShop.Basket.Application\EShop.Basket.Application.csproj
dotnet add reference ..\EShop.Basket.Infrastructure\EShop.Basket.Infrastructure.csproj

REM Ordering Service
echo Ordering Service
cd ..\..\Ordering\EShop.Ordering.Domain
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Domain\EShop.BuildingBlocks.Domain.csproj

cd ..\EShop.Ordering.Application
dotnet add package MediatR --version 12.4.1
dotnet add package FluentValidation --version 11.10.0
dotnet add package MassTransit --version 8.3.3
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Application\EShop.BuildingBlocks.Application.csproj
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Messaging\EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ..\EShop.Ordering.Domain\EShop.Ordering.Domain.csproj

cd ..\EShop.Ordering.Infrastructure
dotnet add package Microsoft.EntityFrameworkCore.Design --version 9.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.0.0
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Infrastructure\EShop.BuildingBlocks.Infrastructure.csproj
dotnet add reference ..\EShop.Ordering.Domain\EShop.Ordering.Domain.csproj

cd ..\EShop.Ordering.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0
dotnet add package MediatR --version 12.4.1
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ..\EShop.Ordering.Application\EShop.Ordering.Application.csproj
dotnet add reference ..\EShop.Ordering.Infrastructure\EShop.Ordering.Infrastructure.csproj

REM Payment Service
echo Payment Service
cd ..\..\Payment\EShop.Payment.Application
dotnet add package MassTransit --version 8.3.3
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Messaging\EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ..\EShop.Payment.Domain\EShop.Payment.Domain.csproj

cd ..\EShop.Payment.Infrastructure
dotnet add reference ..\EShop.Payment.Domain\EShop.Payment.Domain.csproj

cd ..\EShop.Payment.API
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ..\EShop.Payment.Application\EShop.Payment.Application.csproj
dotnet add reference ..\EShop.Payment.Infrastructure\EShop.Payment.Infrastructure.csproj

REM Notification Service
echo Notification Service
cd ..\..\Notification\EShop.Notification.Application
dotnet add package MassTransit --version 8.3.3
dotnet add reference ..\..\..\BuildingBlocks\EShop.BuildingBlocks.Messaging\EShop.BuildingBlocks.Messaging.csproj
dotnet add reference ..\EShop.Notification.Domain\EShop.Notification.Domain.csproj

cd ..\EShop.Notification.Infrastructure
dotnet add package MailKit --version 4.8.0
dotnet add reference ..\EShop.Notification.Domain\EShop.Notification.Domain.csproj

cd ..\EShop.Notification.API
dotnet add package MassTransit.RabbitMQ --version 8.3.3
dotnet add reference ..\EShop.Notification.Application\EShop.Notification.Application.csproj
dotnet add reference ..\EShop.Notification.Infrastructure\EShop.Notification.Infrastructure.csproj

REM API Gateway
echo API Gateway
cd ..\..\..\ApiGateways\EShop.ApiGateway
dotnet add package Yarp.ReverseProxy --version 2.3.0
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 9.0.0

REM Return to root
cd ..\..\..

echo All packages installed!
echo Running dotnet restore...
dotnet restore

echo Building solution...
dotnet build

echo Setup complete!
pause
