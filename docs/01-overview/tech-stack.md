# 🛠️ Технологічний стек

## Backend Stack

### Core Framework
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Runtime | .NET | 9.0 | Основна платформа | Найновіша версія, performance improvements, native AOT |
| Web Framework | ASP.NET Core | 9.0 | REST API, Minimal APIs | Industry standard для .NET web apps |

### API та Communication
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| API Gateway | YARP | 2.x | Reverse proxy, routing | ❌ Ocelot (застарілий), ❌ Kong (overkill) |
| API Documentation | Scalar (OpenAPI) | latest | Interactive API docs | ✅ Swagger UI (але Scalar красивіший) |
| API Versioning | Asp.Versioning | 8.x | URL/Header versioning | - |

### Data Access
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| ORM | Entity Framework Core | 9.0 | Робота з PostgreSQL | ❌ Dapper (less features), ❌ NHibernate (складніший) |
| Database | PostgreSQL | 16 | Primary relational DB | ❌ MySQL (less features), ❌ SQL Server (платний) |
| Cache | Redis | 7 | Distributed caching, sessions | ❌ Memcached (less features) |
| Search Engine | Meilisearch | latest | Full-text search (optional) | ❌ Elasticsearch (heavy), ❌ Algolia (платний) |

### Authentication & Security
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Authentication | ASP.NET Identity | 9.0 | User management | Вбудований у .NET, перевірений часом |
| JWT | System.IdentityModel.Tokens.Jwt | 7.x | Token generation/validation | Industry standard |
| OAuth2 | AspNet.Security.OAuth | 8.x | Google, GitHub login | Official providers |

### Validation та Mapping
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Validation | FluentValidation | 11.x | Model validation | ✅ Data Annotations (але FluentValidation потужніший) |
| Mapping | Mapster | 7.x | Object-to-object mapping | ❌ AutoMapper (повільніший) |

### Messaging та Events
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Message Broker | RabbitMQ | 3.13 | Async communication | ❌ Kafka (складніший для малих проектів), ❌ Azure Service Bus (платний) |
| Event Bus Abstraction | MassTransit | 8.x | Wrapper над RabbitMQ | ❌ NServiceBus (платний), ❌ CAP (менше features) |
| CQRS/Mediator | MediatR | 12.x | Request/response pipeline | Стандарт для .NET |

### Background Jobs
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Job Scheduler | Hangfire | 1.8.x | Background job processing | ❌ Quartz.NET (складніший), ❌ Azure Functions (платний) |

---

## Frontend Stack

### Core Framework
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Framework | React | 18.x | Single Page Application | Найпопулярніший, велика спільнота |
| Language | TypeScript | 5.x | Type safety | Зменшує bugs, кращий DX |
| Build Tool | Vite | 5.x | Fast dev server, bundling | Швидший за Webpack/CRA |

### State Management
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Global State | Zustand | 4.x | Client state management | ❌ Redux (boilerplate heavy), ❌ MobX (складніший) |
| Server State | React Query (TanStack Query) | 5.x | API caching, sync | Industry standard для async state |
| Form State | React Hook Form | 7.x | Form handling | ❌ Formik (менш performant) |

### UI та Styling
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| CSS Framework | Tailwind CSS | 3.x | Utility-first CSS | Швидка розробка, consistent design |
| Component Library | Shadcn/ui | latest | Pre-built components | Customizable, не bloated |
| Icons | Lucide React | latest | Icon library | Красиві, tree-shakeable |

### HTTP та Validation
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| HTTP Client | Axios | 1.x | API requests | ❌ Fetch (менше features) |
| Schema Validation | Zod | 3.x | Runtime type checking | ✅ Yup (але Zod швидший) |

### Routing
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Router | React Router | 6.x | Client-side routing | Industry standard |

### Real-time (Optional)
| Компонент | Технологія | Версія | Призначення | Use Case |
|-----------|------------|--------|-------------|----------|
| WebSocket Client | SignalR Client | 8.x | Real-time updates | Order status updates, notifications |

---

## DevOps та Infrastructure

### Containerization
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Container Runtime | Docker | 24.x | Containerization | Industry standard |
| Orchestration (dev) | Docker Compose | 2.x | Multi-container apps | Простий для локальної розробки |
| Orchestration (prod) | Kubernetes | 1.28+ | Production orchestration (optional) | Scalability, resilience |

### CI/CD
| Компонент | Технологія | Призначення | Чому саме це? |
|-----------|------------|-------------|---------------|
| CI/CD Platform | GitHub Actions | Automated builds, tests, deploy | Інтеграція з GitHub, безкоштовний |
| Container Registry | GitHub Container Registry (ghcr.io) | Docker image storage | Безкоштовний для публічних репо |

### Reverse Proxy (Production)
| Компонент | Технологія | Призначення | Alternatives |
|-----------|------------|-------------|--------------|
| Reverse Proxy | Traefik | Production routing, SSL | ❌ Nginx (складніша конфігурація), ❌ Caddy |
| SSL Certificates | Let's Encrypt | Free HTTPS | - |

---

## Observability Stack

### Logging
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Logging Framework | Serilog | 3.x | Structured logging | Найкращий для .NET |
| Log Sink | Seq | latest | Centralized log server | Зручний UI, безкоштовний для dev |
| Log Enrichers | Serilog.Enrichers.* | - | Context enrichment | CorrelationId, Environment, MachineName |

### Metrics
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Metrics Collector | Prometheus | latest | Time-series metrics | Industry standard |
| Metrics Exporter | prometheus-net | 8.x | .NET metrics exporter | Official .NET client |
| Dashboard | Grafana | latest | Visualization | ❌ Kibana (потрібен Elasticsearch) |

### Distributed Tracing
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Tracing SDK | OpenTelemetry | 1.7+ | Instrumentation | Vendor-neutral standard |
| Trace Backend | Jaeger | latest | Distributed tracing UI | ❌ Zipkin (менше features), ❌ DataDog (платний) |

### Health Checks
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Health Checks | AspNetCore.HealthChecks | 8.x | Service health monitoring | Вбудований у ASP.NET Core |
| Health UI | AspNetCore.HealthChecks.UI | 8.x | Dashboard (optional) | Візуалізація стану сервісів |

---

## Resilience та Reliability

### Resilience Patterns
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Resilience Library | Polly | 8.x | Circuit breaker, retry, timeout | Industry standard для .NET |
| HTTP Resilience | Microsoft.Extensions.Http.Resilience | 8.x | HTTP-specific policies | Вбудований підхід від Microsoft |

---

## Testing Stack

### Testing Frameworks
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Test Framework | xUnit | 2.x | Unit/Integration tests | ❌ NUnit, ❌ MSTest (менше features) |
| Assertion Library | FluentAssertions | 6.x | Readable assertions | Покращує readability тестів |
| Mocking | NSubstitute | 5.x | Test doubles | ❌ Moq (менш зручний API) |

### Integration Testing
| Компонент | Технологія | Версія | Призначення | Чому саме це? |
|-----------|------------|--------|-------------|---------------|
| Test Containers | Testcontainers for .NET | 3.x | Docker-based integration tests | Реальні залежності (Postgres, Redis) у тестах |
| Web App Factory | Microsoft.AspNetCore.Mvc.Testing | 9.0 | In-memory API testing | Вбудований у ASP.NET Core |

### Load Testing
| Компонент | Технологія | Версія | Призначення | Alternatives |
|-----------|------------|--------|-------------|--------------|
| Load Testing | k6 | latest | Performance testing | ❌ JMeter (застарілий UI), ❌ Gatling (складніший) |

### Frontend Testing (Optional)
| Компонент | Технологія | Версія | Призначення | Use Case |
|-----------|------------|--------|-------------|----------|
| Unit Tests | Vitest | 1.x | Component testing | Швидший за Jest |
| E2E Tests | Playwright | 1.x | End-to-end testing (optional) | Cross-browser testing |

---

## Code Quality Tools

### Static Analysis
| Компонент | Технологія | Призначення | Чому саме це? |
|-----------|------------|-------------|---------------|
| .NET Analyzers | Roslyn Analyzers | Code quality checks | Вбудовані у .NET SDK |
| Security Scanner | Trivy | Vulnerability scanning | Docker images + dependencies |
| Dependency Check | Dependabot | Automated dependency updates | GitHub native |

### Code Formatting
| Компонент | Технологія | Призначення | Чому саме це? |
|-----------|------------|-------------|---------------|
| .NET Formatter | dotnet format | C# code formatting | Вбудований у .NET SDK |
| EditorConfig | .editorconfig | Consistent code style | Cross-IDE support |
| Frontend Formatter | Prettier | JS/TS/CSS formatting | Industry standard |
| Frontend Linter | ESLint | JavaScript linting | Ловить помилки |

---

## Порівняння альтернатив

### Чому PostgreSQL, а не MySQL?
| Критерій | PostgreSQL ✅ | MySQL |
|----------|---------------|-------|
| JSONB support | Native + indexing | Limited |
| Window functions | Full support | Partial |
| CTEs (WITH queries) | Recursive support | Basic |
| Full-text search | Built-in | Basic |
| License | Truly open-source | Dual license (Oracle) |

### Чому Mapster, а не AutoMapper?
| Критерій | Mapster ✅ | AutoMapper |
|----------|------------|------------|
| Performance | 2-3x швидший | Baseline |
| API | Fluent + attributes | Profile-based |
| Compile-time check | Code generation | Runtime reflection |
| Learning curve | Простіший | Steep |

### Чому MassTransit, а не чистий RabbitMQ?
| Критерій | MassTransit ✅ | Plain RabbitMQ |
|----------|----------------|----------------|
| Type safety | Strongly-typed messages | Manual serialization |
| Retry policies | Built-in | Manual implementation |
| Saga support | Yes | Manual state machine |
| Observability | OpenTelemetry integration | Manual instrumentation |
| Idempotency | Built-in deduplication | Manual tracking |

### Чому Zustand, а не Redux?
| Критерій | Zustand ✅ | Redux |
|----------|-----------|-------|
| Boilerplate | Minimal | Heavy (actions, reducers) |
| Bundle size | 1 KB | 12 KB+ |
| Learning curve | Easy | Steep |
| DevTools | React DevTools | Redux DevTools |
| Use case | Simple → Medium apps | Large enterprise apps |

---

## Версії та сумісність

### Minimal Requirements
| Компонент | Minimal Version | Recommended |
|-----------|-----------------|-------------|
| .NET SDK | 9.0.0 | Latest 9.x |
| Node.js | 18.x | 20.x LTS |
| Docker | 20.x | Latest |
| PostgreSQL | 14.x | 16.x |
| Redis | 6.x | 7.x |

### Browser Support (Frontend)
| Browser | Minimal Version |
|---------|-----------------|
| Chrome | 90+ |
| Firefox | 88+ |
| Safari | 14+ |
| Edge | 90+ |

---

## Вага та розмір

### Docker Images (Приблизно)
| Image | Size (optimized) |
|-------|------------------|
| Identity API | ~200 MB |
| Catalog API | ~200 MB |
| Ordering API | ~210 MB |
| Basket API | ~180 MB |
| API Gateway | ~180 MB |
| React SPA (nginx) | ~50 MB |

### Build Times
| Project | Build Time (CI) |
|---------|-----------------|
| Backend Service | ~2 min |
| Frontend SPA | ~1 min |
| Full Solution | ~5 min |

---

## Ліцензування

### Open Source (MIT License)
- ✅ .NET, ASP.NET Core
- ✅ PostgreSQL
- ✅ Redis
- ✅ RabbitMQ
- ✅ React
- ✅ Все інше з tech stack

### Free Tiers (для dev)
- ✅ GitHub Actions (2000 хв/місяць)
- ✅ Seq (безкоштовний для single-user)
- ✅ Docker Hub (1 private repo)

### Платні альтернативи НЕ потрібні
- ❌ DataDog (замінено Grafana + Prometheus)
- ❌ New Relic (замінено OpenTelemetry + Jaeger)
- ❌ Azure Application Insights (опційно для production)

---

## Посилання

- [Архітектурна діаграма](architecture-diagram.md)
- [Infrastructure Setup](../05-infrastructure/)
- [ADR: Technology Decisions](../13-appendix/adr/)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
