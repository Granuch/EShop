# 🛒 E-Shop Microservices - Документація

Вітаємо у документації E-Shop Microservices проекту!

## 📖 Навігація

### 🎯 Початок роботи
- [01. Огляд проекту](01-overview/project-overview.md)
- [Архітектурна діаграма](01-overview/architecture-diagram.md)
- [Технологічний стек](01-overview/tech-stack.md)

### 🚀 Швидкий старт
- ✅ [Передумови та встановлення](02-getting-started/prerequisites.md)
- ✅ [Локальне середовище](02-getting-started/local-setup.md) - **NEW!**
- ✅ [Docker налаштування](02-getting-started/docker-setup.md) - **NEW!**
- ✅ [Team Agreement](02-getting-started/team-agreement.md) - **NEW!**

### 🏗️ Архітектура
- [Структура Solution](03-architecture/solution-structure.md)
- [BuildingBlocks](03-architecture/building-blocks.md)
- [Clean Architecture & DDD](03-architecture/clean-architecture.md)
- [Паттерни комунікації](03-architecture/communication-patterns.md)

### 🔧 Сервіси
- ✅ [Identity Service](04-services/identity-service.md) - Автентифікація та авторизація - **NEW!**
- ✅ [Catalog Service](04-services/catalog-service.md) - Каталог продуктів - **NEW!**
- ✅ [Basket Service](04-services/basket-service.md) - Кошик покупок - **NEW!**
- ✅ [Ordering Service](04-services/ordering-service.md) - Обробка замовлень - **NEW!**
- ✅ [Payment Service](04-services/payment-service.md) - Платежі (Mock) - **NEW!**
- ✅ [Notification Service](04-services/notification-service.md) - Email/SMS нотифікації - **NEW!**
- ✅ [API Gateway](04-services/api-gateway.md) - YARP Gateway - **NEW!**

### 💾 Інфраструктура
- [Бази даних](05-infrastructure/databases.md) - PostgreSQL setup
- [Кешування](05-infrastructure/caching.md) - Redis configuration
- [Message Broker](05-infrastructure/message-broker.md) - RabbitMQ & MassTransit
- [Observability](05-infrastructure/observability.md) - Logging, metrics, tracing
- [Resilience](05-infrastructure/resilience.md) - Polly patterns

### 👥 Робочий процес
- [Git Workflow](06-development-workflow/git-workflow.md)
- [Issue Templates](06-development-workflow/issue-templates.md)
- [Pull Request Process](06-development-workflow/pull-request-process.md)
- [Управління задачами](06-development-workflow/task-management.md)

### 📅 План імплементації (56 днів)
- [Phase 0: Infrastructure](07-implementation-plan/phase-00-infrastructure.md) - День 1-3
- [Phase 1: Identity](07-implementation-plan/phase-01-identity.md) - День 4-9
- [Phase 2: Catalog](07-implementation-plan/phase-02-catalog.md) - День 10-15
- [Phase 2.5: Testing](07-implementation-plan/phase-02.5-testing.md) - День 16-17
- [Phase 3: Gateway](07-implementation-plan/phase-03-gateway.md) - День 18-20
- [Phase 4: Basket](07-implementation-plan/phase-04-basket.md) - День 20-23
- [Phase 5: Ordering](07-implementation-plan/phase-05-ordering.md) - День 23-30
- [Phase 6: Payment](07-implementation-plan/phase-06-payment.md) - День 30-32
- [Phase 7: Notification](07-implementation-plan/phase-07-notification.md) - День 32-34
- [Phase 8: Frontend](07-implementation-plan/phase-08-frontend.md) - День 34-43
- [Phase 9: Observability](07-implementation-plan/phase-09-observability.md) - День 43-48
- [Phase 10: Production](07-implementation-plan/phase-10-production.md) - День 48-56

### 🧪 Тестування
- [Стратегія тестування](08-testing/testing-strategy.md)
- [TestContainers Setup](08-testing/testcontainers-setup.md)
- [Load Testing (k6)](08-testing/load-testing.md)

### 🚢 Deployment
- [CI/CD Pipeline](09-deployment/ci-cd-pipeline.md)
- [Docker Deployment](09-deployment/docker-deployment.md)
- [Kubernetes Deployment](09-deployment/kubernetes-deployment.md)
- [Migration Strategy](09-deployment/migration-strategy.md)

### 🔐 Production Readiness
- [Security Hardening](10-production-readiness/security-hardening.md)
- [Monitoring & Alerts](10-production-readiness/monitoring-alerts.md)
- [Disaster Recovery](10-production-readiness/disaster-recovery.md)
- [Performance Benchmarks](10-production-readiness/performance-benchmarks.md)
- [API Versioning](10-production-readiness/api-versioning.md)

### 🔧 Troubleshooting
- [Типові проблеми](11-troubleshooting/common-issues.md)
- [Debugging Guide](11-troubleshooting/debugging-guide.md)
- [Diagnostic Scripts](11-troubleshooting/diagnostic-scripts.md)

### 👥 Командна робота
- [Інструменти комунікації](12-team-collaboration/communication-tools.md)
- [Розклад зустрічей](12-team-collaboration/meeting-schedule.md)
- [Onboarding Checklist](12-team-collaboration/onboarding-checklist.md)
- [Розподіл ролей](12-team-collaboration/role-distribution.md)

### 📚 Додатки
- [Глосарій](13-appendix/glossary.md)
- [Ресурси та посилання](13-appendix/resources.md)
- [Architecture Decision Records](13-appendix/adr/)
- [Post-MVP Roadmap](13-appendix/roadmap.md)
- [Success Criteria](13-appendix/success-criteria.md)

---

## 🎯 Швидкі посилання

### Для новачків
1. 📖 [Огляд проекту](01-overview/project-overview.md)
2. 💻 [Передумови встановлення](02-getting-started/prerequisites.md)
3. 🚀 [Локальний запуск](02-getting-started/local-setup.md)
4. 👋 [Onboarding](12-team-collaboration/onboarding-checklist.md)

### Для розробників
1. 🌳 [Git Workflow](06-development-workflow/git-workflow.md)
2. 🔍 [Pull Request Process](06-development-workflow/pull-request-process.md)
3. 🐛 [Debugging Guide](11-troubleshooting/debugging-guide.md)

### Для DevOps
1. 🐳 [Docker Deployment](09-deployment/docker-deployment.md)
2. ⚙️ [CI/CD Pipeline](09-deployment/ci-cd-pipeline.md)
3. 📊 [Monitoring](10-production-readiness/monitoring-alerts.md)

---

## 📞 Контакти та підтримка

- **GitHub Issues**: Повідомлення про баги та нові фічі
- **GitHub Discussions**: Архітектурні питання та пропозиції
- **Discord/Slack**: Щоденна комунікація команди

---

**Версія документації**: 1.0  
**Остання оновлення**: 2024-01-27  
**Ліцензія**: MIT
