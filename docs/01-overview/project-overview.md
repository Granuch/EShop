# 📖 Огляд проекту

## Назва проекту

**E-Shop Microservices** (Codename)

## Тип проекту

Інтернет-магазин (прототип без реального платіжного сервісу)

## Архітектурний підхід

Мікросервісна архітектура

## Рівень зрілості

Production-ready прототип

---

## 🎯 Цілі проекту

### Основна мета
Створити повнофункціональний інтернет-магазин на основі мікросервісної архітектури, який демонструє enterprise-level best practices та сучасні підходи до розробки.

### Навчальні цілі
1. **Освоїти мікросервісну архітектуру** - розуміння принципів побудови розподілених систем
2. **Практика з .NET 9** - використання найновіших можливостей платформи
3. **Event-Driven Architecture** - асинхронна комунікація через message bus
4. **Clean Architecture & DDD** - правильна організація коду та бізнес-логіки
5. **DevOps практики** - CI/CD, containerization, monitoring
6. **Командна розробка** - Git workflow, code review, documentation

### Бізнес-цілі (для демо)
- Каталог продуктів з пошуком та фільтрацією
- Кошик покупок з можливістю оформлення замовлення
- Система автентифікації та авторизації користувачів
- Обробка замовлень з різними статусами
- Email-нотифікації про події
- Адміністративна панель для управління

---

## 🌟 Основні можливості

### Для користувачів (Customers)
- ✅ Реєстрація та вхід в систему
- ✅ Перегляд каталогу продуктів
- ✅ Пошук та фільтрація товарів
- ✅ Додавання товарів до кошика
- ✅ Оформлення замовлення
- ✅ Перегляд історії замовлень
- ✅ Управління профілем
- ✅ Email-нотифікації про статус замовлення

### Для адміністраторів (Admins)
- ✅ Управління каталогом продуктів (CRUD)
- ✅ Управління категоріями
- ✅ Перегляд всіх замовлень
- ✅ Зміна статусів замовлень
- ✅ Управління користувачами та ролями
- ✅ Перегляд статистики

### Технічні можливості
- ✅ JWT-based автентифікація з refresh tokens
- ✅ 2FA (Two-Factor Authentication) через TOTP
- ✅ OAuth2 інтеграція (Google, GitHub)
- ✅ Розподілене кешування (Redis)
- ✅ Асинхронна обробка подій (RabbitMQ + MassTransit)
- ✅ Distributed tracing (OpenTelemetry + Jaeger)
- ✅ Centralized logging (Serilog + Seq)
- ✅ Metrics та dashboards (Prometheus + Grafana)
- ✅ Health checks та resilience patterns
- ✅ API versioning
- ✅ Rate limiting
- ✅ CORS configuration

---

## 🚫 Що НЕ входить в scope

Для фокусування на ключових аспектах, наступні функції **не реалізовані**:

### Платіжна інтеграція
- ❌ Реальний платіжний шлюз (Stripe, PayPal) - замість цього **mock payment service**
- ❌ PCI DSS compliance
- ❌ Зберігання карткових даних

### Shipping та логістика
- ❌ Інтеграція з перевізниками
- ❌ Tracking відправлень
- ❌ Складський облік (inventory management базовий)

### Advanced features
- ❌ Реальна повнотекстова пошукова система (Elasticsearch/Meilisearch опційно)
- ❌ Recommendation engine (ML-based)
- ❌ Real-time chat support
- ❌ Mobile додаток
- ❌ Multi-tenancy
- ❌ Internationalization (i18n) - тільки українська/англійська

### Infrastructure
- ❌ Multi-region deployment
- ❌ Auto-scaling в production (тільки manual scaling)
- ❌ Advanced security (WAF, DDoS protection)

> **Примітка**: Деякі з цих функцій можуть бути додані в майбутньому (див. [Roadmap](../13-appendix/roadmap.md))

---

## 📊 Технічні характеристики

### Performance Targets
- **API Response Time**: < 200ms (p95)
- **Database Queries**: < 50ms (p95)
- **Cache Hit Rate**: > 80%
- **Concurrent Users**: 100+ (load test verified)

### Scalability
- **Horizontal scaling**: Всі сервіси можуть мати кілька інстансів
- **Database**: Підтримка read replicas (prepared)
- **Cache**: Redis Cluster support (ready)
- **Message Bus**: RabbitMQ clustered mode (configured)

### Reliability
- **Uptime Target**: 99.9% (SLA)
- **Error Rate**: < 0.1%
- **RTO (Recovery Time Objective)**: 15 хвилин для critical services
- **RPO (Recovery Point Objective)**: 0 для transactional data (WAL archiving)

### Security
- **Authentication**: JWT with RS256 signing (prepared for key rotation)
- **Authorization**: Role-based + Claims-based
- **Data Protection**: Personal data anonymization
- **HTTPS**: Enforced everywhere
- **Rate Limiting**: Per-user and global
- **Security Headers**: CSP, HSTS, X-Frame-Options, etc.

---

## 🏛️ Архітектурні принципи

### 1. Bounded Contexts (DDD)
Кожен сервіс має власний bounded context:
- **Identity**: User management, authentication
- **Catalog**: Products, categories
- **Basket**: Shopping cart
- **Ordering**: Orders, order processing
- **Payment**: Payment processing (mock)
- **Notification**: Email/SMS sending

### 2. Database per Service
Кожен сервіс має власну базу даних (або схему):
- Identity → `identity` database
- Catalog → `catalog` database
- Ordering → `ordering` database
- Basket → Redis (in-memory)

### 3. Event-Driven Communication
Сервіси комунікують через events (RabbitMQ):
- `BasketCheckoutEvent` → Order Service
- `OrderCreatedEvent` → Notification Service
- `PaymentCompletedEvent` → Order Service

### 4. API Gateway Pattern
Один вхід для frontend:
- YARP reverse proxy
- Authentication/Authorization
- Rate limiting
- Request routing

### 5. Resilience Patterns
- **Circuit Breaker**: Запобігання cascade failures
- **Retry with exponential backoff**: Для transient failures
- **Timeout policies**: Для всіх зовнішніх викликів
- **Bulkhead isolation**: Resource limits per service

---

## 🎓 Навчальна цінність

Цей проект демонструє:

### Backend Skills
- ✅ Мікросервісна архітектура з нуля
- ✅ Clean Architecture (Domain, Application, Infrastructure layers)
- ✅ Domain-Driven Design (Aggregates, Value Objects, Domain Events)
- ✅ CQRS pattern (MediatR)
- ✅ Event Sourcing basics
- ✅ Asynchronous messaging (RabbitMQ + MassTransit)
- ✅ Entity Framework Core advanced features
- ✅ Repository and Unit of Work patterns
- ✅ Specification pattern

### DevOps Skills
- ✅ Docker & Docker Compose
- ✅ CI/CD pipelines (GitHub Actions)
- ✅ Infrastructure as Code готовність
- ✅ Monitoring та observability
- ✅ Log aggregation
- ✅ Distributed tracing

### Frontend Skills (React)
- ✅ React 18 з hooks
- ✅ State management (Zustand)
- ✅ API integration (React Query + Axios)
- ✅ Form validation (React Hook Form + Zod)
- ✅ Modern UI (Shadcn/ui + Tailwind CSS)

### Soft Skills
- ✅ Technical documentation
- ✅ Code review process
- ✅ Git workflow (feature branches, PRs)
- ✅ Team collaboration
- ✅ Task estimation і planning

---

## 📈 Метрики успіху проекту

Проект вважається успішним, якщо:

### Технічні метрики
- [ ] ✅ Всі 7 мікросервісів працюють і взаємодіють
- [ ] ✅ Code coverage > 70% (unit + integration tests)
- [ ] ✅ CI/CD pipeline автоматично деплоїть зміни
- [ ] ✅ Zero critical security vulnerabilities
- [ ] ✅ Performance benchmarks виконуються

### Функціональні метрики
- [ ] ✅ Користувач може пройти повний flow: реєстрація → каталог → кошик → замовлення
- [ ] ✅ Адмін може управляти продуктами та замовленнями
- [ ] ✅ Email-нотифікації працюють
- [ ] ✅ Моніторинг показує здоров'я системи

### Командні метрики
- [ ] ✅ Всі члени команди зробили мінімум 10 PR
- [ ] ✅ Документація актуальна і зрозуміла
- [ ] ✅ Код пройшов code review
- [ ] ✅ Є культура взаємодопомоги

### Portfolio метрики
- [ ] ✅ Проект задеплоєний і доступний онлайн
- [ ] ✅ Є README з чіткими інструкціями
- [ ] ✅ Є архітектурна діаграма
- [ ] ✅ Можна показати рекрутерам

---

## 🔄 Життєвий цикл проекту

### Phase 0: Підготовка (3 дні)
- Налаштування інфраструктури
- BuildingBlocks
- Team Agreement

### Phase 1-7: Core Development (30 днів)
- Розробка мікросервісів один за одним
- Integration testing
- Event-driven integration

### Phase 8: Frontend (9 днів)
- React SPA
- UI/UX implementation

### Phase 9-10: Production Ready (13 днів)
- Observability
- Security hardening
- Performance optimization
- Documentation

### Post-MVP: Maintenance
- Bug fixes
- Performance tuning
- New features з roadmap

---

## 📞 Корисні посилання

- [Архітектурна діаграма](architecture-diagram.md)
- [Технологічний стек](tech-stack.md)
- [План реалізації](../07-implementation-plan/phase-00-infrastructure.md)
- [Roadmap](../13-appendix/roadmap.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
