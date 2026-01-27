# 🛒 E-Shop Microservices

Production-ready інтернет-магазин на мікросервісній архітектурі з .NET 9 та React.

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![React](https://img.shields.io/badge/React-18-61DAFB?logo=react)](https://reactjs.org/)
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
- **[Сервіси](docs/04-services/)** - Документація кожного мікросервісу  
- **[План реалізації](docs/07-implementation-plan/)** - Покрокова інструкція (56 днів)  
- **[Roadmap](docs/13-appendix/roadmap.md)** - Майбутні features  

---

## 🎯 Що це за проект?

**E-Shop Microservices** - це навчальний проект full-stack інтернет-магазину для демонстрації enterprise-level підходів:

### Ключові особливості
- ✅ **7 мікросервісів** - Identity, Catalog, Basket, Ordering, Payment, Notification, API Gateway  
- ✅ **Event-Driven архітектура** - Асинхронна комунікація через RabbitMQ + MassTransit  
- ✅ **Clean Architecture & DDD** - Domain-driven design, CQRS, Aggregates  
- ✅ **Full Observability** - Logs (Seq), Metrics (Prometheus/Grafana), Distributed Tracing (Jaeger)  
- ✅ **Resilience Patterns** - Circuit Breaker, Retry, Timeout (Polly)  
- ✅ **Production-ready** - Security hardening, monitoring, disaster recovery  
- ✅ **CI/CD** - GitHub Actions, Docker, automated tests  

### Що можна робити
**Для користувачів**:
- 🛍️ Реєстрація та вхід (JWT + 2FA)
- 📦 Перегляд каталогу продуктів
- 🔍 Пошук та фільтрація
- 🛒 Додавання товарів до кошика
- ✅ Оформлення замовлення
- 📧 Email нотифікації про статус

**Для адміністраторів**:
- ➕ CRUD операції над продуктами
- 📊 Перегляд усіх замовлень
- 👥 Управління користувачами

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
- **.NET 9** - Web API, Minimal APIs
- **PostgreSQL** - Primary database
- **Redis** - Caching + Basket storage
- **RabbitMQ** - Message broker
- **MassTransit** - Event bus abstraction
- **Entity Framework Core** - ORM
- **MediatR** - CQRS implementation

### Frontend
- **React 18** + TypeScript
- **Zustand** - State management
- **React Query** - Server state
- **Tailwind CSS** + Shadcn/ui - Styling

### Observability
- **Serilog + Seq** - Structured logging
- **Prometheus + Grafana** - Metrics
- **OpenTelemetry + Jaeger** - Distributed tracing

**Повний стек**: [docs/01-overview/tech-stack.md](docs/01-overview/tech-stack.md)

---

## 🚀 Швидкий старт (5 хвилин)

### Передумови
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Node.js 20+](https://nodejs.org/)
- [Git](https://git-scm.com/)

**Детальні інструкції**: [docs/02-getting-started/prerequisites.md](docs/02-getting-started/prerequisites.md)

### Запуск проекту

```bash
# 1. Клонувати репозиторій
git clone https://github.com/your-username/eshop-microservices.git
cd eshop-microservices

# 2. Запустити інфраструктуру (PostgreSQL, Redis, RabbitMQ, Seq)
cd deploy/docker
docker compose up -d

# 3. Запустити сервіси (у окремих терміналах)
cd ../../src/Services/Identity/EShop.Identity.API
dotnet run

cd ../Catalog/EShop.Catalog.API
dotnet run

# ... або через docker-compose (рекомендовано)
cd deploy/docker
docker compose -f docker-compose.yml -f docker-compose.override.yml up

# 4. Запустити Frontend
cd ../../src/Web/eshop-spa
npm install
npm run dev
```

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
└── docs/                  # 📖 Вся документація
```

**Детальна структура**: [docs/03-architecture/solution-structure.md](docs/03-architecture/solution-structure.md)

---

## 📅 План розробки (56 днів)

| Phase | Назва | Тривалість | Статус |
|-------|-------|------------|--------|
| 0 | Infrastructure Setup | 3 дні | ⏳ Planned |
| 1 | Identity Service | 6 днів | 📋 Planned |
| 2 | Catalog Service | 6 днів | 📋 Planned |
| 2.5 | Testing Infrastructure | 2 дні | 📋 Planned |
| 3 | API Gateway | 3 дні | 📋 Planned |
| 4 | Basket Service | 3 дні | 📋 Planned |
| 5 | Ordering Service | 7 днів | 📋 Planned |
| 6 | Payment Service | 2 дні | 📋 Planned |
| 7 | Notification Service | 2 дні | 📋 Planned |
| 8 | Frontend (React) | 9 днів | 📋 Planned |
| 9 | Observability | 5 днів | 📋 Planned |
| 10 | Production Ready | 8 днів | 📋 Planned |

**Детальний план**: [docs/07-implementation-plan/](docs/07-implementation-plan/)

---

## 🧪 Тестування

```bash
# Unit tests
dotnet test tests/UnitTests/

# Integration tests (з TestContainers)
dotnet test tests/IntegrationTests/

# Load tests (k6)
k6 run tests/LoadTests/catalog-load-test.js
```

**Testing Strategy**: [docs/08-testing/testing-strategy.md](docs/08-testing/testing-strategy.md)

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
- ✅ Код компілюється без помилок
- ✅ Тести проходять
- ✅ Code review пройдено (мінімум 1 approve)
- ✅ PR опис заповнений ([template](.github/pull_request_template.md))

**Детальніше**: [docs/06-development-workflow/pull-request-process.md](docs/06-development-workflow/pull-request-process.md)

---

## 👥 Team Agreement

Перед початком роботи обов'язково ознайомтеся з:

- [Team Agreement](docs/02-getting-started/team-agreement.md) - Coding conventions, PR rules
- [Git Workflow](docs/06-development-workflow/git-workflow.md) - Branching strategy
- [Task Management](docs/06-development-workflow/task-management.md) - GitHub Projects

---

## 📊 Моніторинг та Observability

- **Logs**: Seq - http://localhost:5341
- **Metrics**: Grafana - http://localhost:3000 (admin/admin)
- **Traces**: Jaeger - http://localhost:16686
- **Health**: http://localhost:5000/health

**Налаштування**: [docs/05-infrastructure/observability.md](docs/05-infrastructure/observability.md)

---

## 🔒 Security

- ✅ JWT токени з key rotation
- ✅ 2FA (TOTP)
- ✅ Rate limiting
- ✅ Security headers (CSP, HSTS, X-Frame-Options)
- ✅ Secrets management (Azure Key Vault ready)
- ✅ HTTPS enforced

**Security Guide**: [docs/10-production-readiness/security-hardening.md](docs/10-production-readiness/security-hardening.md)

---

## 🔮 Roadmap

**Post-MVP features**:
- Admin Dashboard (React)
- Real Payment Gateway (Stripe)
- Promo Codes / Discounts
- Customer Reviews & Ratings
- Recommendation Engine (ML.NET)
- Mobile App (React Native / MAUI)

**Повний roadmap**: [docs/13-appendix/roadmap.md](docs/13-appendix/roadmap.md)

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

**Більше ресурсів**: [docs/13-appendix/resources.md](docs/13-appendix/resources.md)

---

## 📞 Контакти та підтримка

- **GitHub Issues**: [Create Issue](https://github.com/your-username/eshop-microservices/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-username/eshop-microservices/discussions)
- **Discord**: [Join Server](#)

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

**Версія проекту**: 1.0.0  
**Остання оновлення документації**: 2024-01-15

**⭐ Якщо проект корисний - поставте зірочку на GitHub!**
