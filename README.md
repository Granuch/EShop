# 🛒 E-Shop Microservices

Production-ready інтернет-магазин на мікросервісній архітектурі з .NET 10 та React.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?logo=docker)](https://www.docker.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 📖 Документація

**📚 Повна документація доступна у папці [`docs/`](docs/README.md)**

### 🚀 Швидкий старт
1. [Передумови](docs/02-getting-started/prerequisites.md) - Встановлення необхідного ПЗ  
2. [Локальне налаштування](docs/02-getting-started/local-setup.md) - Запуск проекту  
3. [Docker Setup](docs/02-getting-started/docker-setup.md) - Запуск через Docker  

### 📚 Основні розділи
- **[Огляд проекту](docs/01-overview/project-overview.md)** - Цілі, scope, метрики успіху  
- **[Архітектура](docs/01-overview/architecture-diagram.md)** - Діаграми та пояснення компонентів  
- **[Технологічний стек](docs/01-overview/tech-stack.md)** - Детальний опис усіх технологій  
- **[Сервіси](docs/05-services/)** - Документація кожного мікросервісу  
- **[План реалізації](docs/04-implementation-plan/)** - Покрокова інструкція (56 днів)  
- **[Roadmap](docs/09-appendix/roadmap.md)** - Майбутні features  

---

## 🎯 Що це за проект?

**E-Shop Microservices** - це навчальний проект full-stack інтернет-магазину для демонстрації enterprise-level підходів:

### Ключові особливості
- **7 мікросервісів** - Identity, Catalog, Basket, Ordering, Payment, Notification, API Gateway  
- **Event-Driven архітектура** - Асинхронна комунікація через RabbitMQ + MassTransit  
- **Clean Architecture & DDD** - Domain-driven design, CQRS, Aggregates  
- **Full Observability** - Logs (Seq), Metrics (Prometheus/Grafana), Distributed Tracing (Jaeger)  
- **Resilience Patterns** - Circuit Breaker, Retry, Timeout (Polly)  
- **Production-ready** - Security hardening, monitoring, disaster recovery  
- **CI/CD** - GitHub Actions, Docker, automated tests  

### Що можна робити
**Для користувачів**:
- Реєстрація та вхід (JWT + 2FA)
- Перегляд каталогу продуктів
- Пошук та фільтрація
- Додавання товарів до кошика
- Оформлення замовлення
- Email нотифікації про статус

**Для адміністраторів**:
- CRUD операції над продуктами
- Перегляд усіх замовлень
- Управління користувачами

---

## 🏛️ Архітектура

```
Frontend (React SPA)
       ↓
API Gateway (YARP) → Rate Limiting, JWT Validation
       ↓
┌──────┴──────┬─────────┬─────────┬──────────┐
│             │         │         │          │
Identity   Catalog   Basket   Ordering   Payment
  ↓           ↓         ↓         ↓          ↓
PostgreSQL  PostgreSQL  Redis  PostgreSQL  Mock
                ↓                  ↓
              Cache           RabbitMQ
                                  ↓
                           Notification
```

**Детальні діаграми**: [docs/01-overview/architecture-diagram.md](docs/01-overview/architecture-diagram.md)

---

## 🛠️ Технології

### Backend
- **.NET 10** - Web API, Minimal APIs
- **PostgreSQL** - Primary database
- **Redis** - Caching + Basket storage
- **RabbitMQ** - Message broker
- **MassTransit** - Event bus abstraction
- **Entity Framework Core** - ORM
- **MediatR** - CQRS implementation

### Observability
- **Serilog + Seq** - Structured logging
- **Prometheus + Grafana** - Metrics
- **OpenTelemetry + Jaeger** - Distributed tracing

**Повний стек**: [docs/01-overview/tech-stack.md](docs/01-overview/tech-stack.md)

---

## 🚀 Швидкий старт (5 хвилин)

### Передумови
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Git](https://git-scm.com/)

**Детальні інструкції**: [docs/02-getting-started/prerequisites.md](docs/02-getting-started/prerequisites.md)

**Endpoints**:
- Frontend: http://localhost:3000
- API Gateway: http://localhost:5000
- Seq (Logs): http://localhost:5341
- Grafana: http://localhost:3000
- RabbitMQ Management: http://localhost:15672 (guest/guest)

**Повна інструкція**: [docs/02-getting-started/local-setup.md](docs/02-getting-started/local-setup.md)

---

## 📂 Структура проекту

```
EShop/
├── src/
│   ├── Services/          # 7 мікросервісів
│   │   ├── Identity/      # Auth (JWT, 2FA, OAuth2)
│   │   ├── Catalog/       # Products, Categories
│   │   ├── Basket/        # Shopping Cart (Redis)
│   │   ├── Ordering/      # Orders (CQRS + DDD)
│   │   ├── Payment/       # Mock payment processing
│   │   └── Notification/  # Email worker
│   ├── ApiGateway/        # YARP reverse proxy
│   ├── BuildingBlocks/    # Shared libraries
│   └── Web/eshop-spa/     # React frontend
├── tests/                 # Unit + Integration tests
├── deploy/docker/         # Docker Compose configs
└── docs/                  # Вся документація
```

**Детальна структура**: [docs/03-architecture/solution-structure.md](docs/03-architecture/solution-structure.md)

---

## 🤝 Як контриб'ютити

### Git Workflow
```bash
# 1. Створити feature branch
git checkout -b feature/my-awesome-feature

# 2. Зробити зміни та закомітити
git add .
git commit -m "feat: add awesome feature"

# 3. Push та створити Pull Request
git push origin feature/my-awesome-feature
```

### Pull Request Guidelines
- Код компілюється без помилок
- Тести проходять
- Code review пройдено (мінімум 1 approve)
- PR опис заповнений ([template](.github/pull_request_template.md))

**Детальніше**: [docs/07-development-workflow/pull-request-process.md](docs/07-development-workflow/pull-request-process.md)

---

## 👥 Team Agreement

Перед початком роботи обов'язково ознайомтеся з:

- [Team Agreement](docs/02-getting-started/team-agreement.md) - Coding conventions, PR rules
- [Git Workflow](docs/07-development-workflow/git-workflow.md) - Branching strategy
- [Task Management](docs/07-development-workflow/task-management.md) - GitHub Projects

---

## 📊 Моніторинг та Observability

- **Logs**: Seq - http://localhost:5341
- **Metrics**: Grafana - http://localhost:3000 (admin/admin)
- **Traces**: Jaeger - http://localhost:16686
- **Health**: http://localhost:5000/health

**Налаштування**: [docs/06-infrastructure/observability.md](docs/06-infrastructure/observability.md)

---

## 🔒 Security

- JWT токени з key rotation
- Rate limiting
- Security headers (CSP, HSTS, X-Frame-Options)
- Secrets management (Azure Key Vault ready)
- HTTPS enforced

**Security Guide**: [docs/10-production-readiness/security-hardening.md](docs/10-production-readiness/security-hardening.md)

---

## 📚 Корисні ресурси

### Офіційна документація
- [.NET Microservices Guide](https://docs.microsoft.com/en-us/dotnet/architecture/microservices/)
- [eShopOnContainers](https://github.com/dotnet-architecture/eShopOnContainers) - Microsoft reference app
- [MassTransit Docs](https://masstransit-project.com/)

### Книги
- **Microservices Patterns** - Chris Richardson
- **Building Microservices** - Sam Newman
- **Domain-Driven Design** - Eric Evans
- **Clean Architecture** - Robert C. Martin

**Більше ресурсів**: [docs/09-appendix/resources.md](docs/09-appendix/resources.md)

---

## 📄 Ліцензія

Цей проект ліцензовано під MIT License - див. [LICENSE](LICENSE) файл для деталей.

---

## 🌟 Подяки

Проект натхненний:
- [eShopOnContainers](https://github.com/dotnet-architecture/eShopOnContainers) by Microsoft
- [Clean Architecture Template](https://github.com/jasontaylordev/CleanArchitecture) by Jason Taylor
- [Microservices Demo](https://github.com/GoogleCloudPlatform/microservices-demo) by Google Cloud

---

**Остання оновлення документації**: 2024-01-15
