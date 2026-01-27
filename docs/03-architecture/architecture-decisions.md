# 📋 Architecture Decision Records (ADRs)

ADR (Architecture Decision Record) - документування важливих архітектурних рішень.

**Формат**: [MADR](https://adr.github.io/madr/) (Markdown Any Decision Records)

---

## ADR Template

```markdown
# ADR-XXX: [Title]

**Status**: [Proposed | Accepted | Deprecated | Superseded]  
**Date**: YYYY-MM-DD  
**Deciders**: [Names]  
**Technical Story**: [Link to JIRA/GitHub Issue]

## Context

[Describe the problem and context]

## Decision

[Describe the decision]

## Consequences

**Positive:**
- [Pro 1]
- [Pro 2]

**Negative:**
- [Con 1]
- [Con 2]

**Risks:**
- [Risk 1]
- [Risk 2]

## Alternatives Considered

### Alternative 1: [Name]
[Description]
**Pros**: ...  
**Cons**: ...

### Alternative 2: [Name]
[Description]
**Pros**: ...  
**Cons**: ...

## References

- [Link 1]
- [Link 2]
```

---

## ADR-001: Use Microservices Architecture

**Status**: Accepted  
**Date**: 2024-01-10  
**Deciders**: Tech Lead, Senior Developers  

### Context

We need to choose an architectural style for our e-commerce platform. Requirements:
- Support 10,000+ concurrent users
- Frequent deployments (multiple times per day)
- Different teams working independently
- Scalability (Black Friday traffic spikes)

### Decision

We will use **Microservices Architecture** with the following services:
- Identity (Authentication)
- Catalog (Products)
- Basket (Shopping Cart)
- Ordering (Orders)
- Payment (Payments)
- Notification (Emails/SMS)
- API Gateway (Single entry point)

### Consequences

**Positive:**
- **Independent deployment**: Each service can be deployed separately
- **Technology freedom**: Can use different tech stacks per service
- **Scalability**: Scale only services that need it (e.g., Catalog during sales)
- **Team autonomy**: Each team owns a service
- **Fault isolation**: Failure in one service doesn't crash entire system

**Negative:**
- **Complexity**: More moving parts (7 services + infrastructure)
- **Distributed systems challenges**: Network latency, eventual consistency
- **Testing complexity**: Need integration tests across services
- **Operational overhead**: Monitoring, logging, tracing for all services
- **Data consistency**: No ACID transactions across services

**Risks:**
- **Team learning curve**: Team needs to learn distributed systems patterns
- **Infrastructure costs**: More servers, containers, databases
- **Debugging difficulty**: Harder to trace requests across services

### Alternatives Considered

#### Alternative 1: Monolithic Architecture

**Description**: Single application with all features.

**Pros**:
- Simpler development and deployment
- ACID transactions
- Easier debugging
- Lower infrastructure costs

**Cons**:
- **Cannot scale independently** (must scale entire app)
- Single point of failure
- Slow deployments (entire app)
- Team conflicts (all working in same codebase)
- Technology lock-in

**Why rejected**: Doesn't meet scalability and team autonomy requirements.

#### Alternative 2: Modular Monolith

**Description**: Single deployment unit but modular codebase.

**Pros**:
- Simpler than microservices
- ACID transactions
- Can evolve to microservices later

**Cons**:
- Still single deployment
- Cannot scale modules independently
- Harder to enforce boundaries

**Why rejected**: Doesn't solve independent scaling requirement.

### References

- [Microservices by Martin Fowler](https://martinfowler.com/articles/microservices.html)
- [Building Microservices - Sam Newman](https://www.oreilly.com/library/view/building-microservices/9781491950340/)

---

## ADR-002: Use Clean Architecture for Services

**Status**: Accepted  
**Date**: 2024-01-10  
**Deciders**: Tech Lead

### Context

We need a consistent internal architecture for all microservices. Goals:
- Testability
- Separation of concerns
- Independence from frameworks
- Maintainability

### Decision

All services will follow **Clean Architecture** (aka Onion Architecture) with 4 layers:

1. **Domain Layer**: Business entities, value objects, domain events
2. **Application Layer**: Use cases (CQRS commands/queries)
3. **Infrastructure Layer**: Database, external APIs, message broker
4. **API Layer**: Controllers, endpoints, middleware

**Dependency rule**: Outer layers depend on inner layers, never the reverse.

### Consequences

**Positive:**
- **Testability**: Domain logic isolated, easy to unit test
- **Framework independence**: Can replace EF Core with Dapper without changing domain
- **Clear separation**: Each layer has single responsibility
- **Consistency**: Same structure across all services

**Negative:**
- **More boilerplate**: Interfaces, DTOs, mappings
- **Learning curve**: Team needs to understand architecture
- **Slower initial development**: More setup than "quick and dirty"

**Risks:**
- Team over-engineering simple features
- Inconsistent implementation across services

### Alternatives Considered

#### Alternative 1: N-Tier Architecture

**Pros**: Simple, widely understood  
**Cons**: Tight coupling, hard to test, framework-dependent

#### Alternative 2: Vertical Slice Architecture

**Pros**: Feature-focused, less abstraction  
**Cons**: Code duplication, harder to enforce consistency

### References

- [Clean Architecture - Robert C. Martin](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [.NET Microservices - Clean Architecture](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/)

---

## ADR-003: Use PostgreSQL for Primary Database

**Status**: Accepted  
**Date**: 2024-01-11

### Context

Need to choose a database for transactional data (orders, users, products).

**Requirements**:
- ACID transactions
- Complex queries (joins, aggregations)
- JSON support (for flexible schemas)
- Open-source
- Good performance

### Decision

Use **PostgreSQL 16** as primary database for:
- Identity Service (users, roles)
- Catalog Service (products, categories)
- Ordering Service (orders)
- Payment Service (payment records)

Each service has **its own database** (Database-per-Service pattern).

### Consequences

**Positive:**
- **ACID compliance**: Strong consistency guarantees
- **Rich feature set**: Full-text search, JSON, arrays, CTEs
- **Open source**: No licensing costs
- **Mature ecosystem**: ORMs (EF Core, Dapper), tools (pgAdmin)
- **Performance**: Good for transactional workloads
- **Extensibility**: PostGIS, pg_stat_statements, etc.

**Negative:**
- **Operational complexity**: Need to manage multiple databases
- **No cross-database transactions**: Need saga pattern for distributed transactions
- **Scaling writes**: Harder than NoSQL databases

**Risks:**
- Data duplication across services
- Eventual consistency challenges

### Alternatives Considered

#### Alternative 1: MongoDB

**Pros**: Flexible schema, easy horizontal scaling  
**Cons**: Weak consistency, no joins, not suitable for transactional data

#### Alternative 2: SQL Server

**Pros**: Enterprise features, great tooling  
**Cons**: Licensing costs, less portable

#### Alternative 3: MySQL

**Pros**: Widely used, simple  
**Cons**: Less feature-rich than PostgreSQL

### References

- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [Database per Service Pattern](https://microservices.io/patterns/data/database-per-service.html)

---

## ADR-004: Use Redis for Caching and Session Storage

**Status**: Accepted  
**Date**: 2024-01-11

### Context

Need caching for:
- Product listings (read-heavy)
- User sessions
- Shopping basket (ephemeral data)
- Rate limiting counters

### Decision

Use **Redis** for:
1. **Distributed cache** (Catalog, Identity services)
2. **Primary storage for Basket Service** (no SQL database)
3. **Rate limiting** (API Gateway)
4. **Refresh token storage** (Identity Service)

### Consequences

**Positive:**
- **High performance**: In-memory, sub-millisecond latency
- **Rich data structures**: Strings, hashes, sets, sorted sets
- **Pub/Sub**: Can use for real-time features
- **Persistence**: AOF + RDB for durability
- **Widely supported**: Libraries for .NET, React

**Negative:**
- **Memory cost**: More expensive than disk storage
- **Data size limits**: Not suitable for large objects
- **Single point of failure**: Need Redis Cluster for HA

**Risks:**
- Cache invalidation complexity
- Memory pressure on high traffic

### Alternatives Considered

#### Alternative 1: Memcached

**Pros**: Simple, fast  
**Cons**: Limited data structures, no persistence

#### Alternative 2: In-memory cache only (IMemoryCache)

**Pros**: No external dependency  
**Cons**: Not shared across instances, lost on restart

### References

- [Redis Documentation](https://redis.io/docs/)
- [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/)

---

## ADR-005: Use RabbitMQ for Async Communication

**Status**: Accepted  
**Date**: 2024-01-12

### Context

Services need to communicate asynchronously (e.g., Order created → Send email).

**Requirements**:
- Reliable message delivery
- At-least-once delivery guarantee
- Dead-letter queues
- Message routing

### Decision

Use **RabbitMQ** with **MassTransit** library for:
- Integration events (OrderCreated, PaymentSuccess, etc.)
- Command dispatch (ProcessPayment)
- Background job scheduling

**Message patterns**:
- **Publish/Subscribe**: One event, multiple consumers
- **Request/Response**: Sync-like behavior over async

### Consequences

**Positive:**
- **Decoupling**: Services don't need to know about each other
- **Reliability**: Messages persisted to disk
- **Retry logic**: Automatic retries with MassTransit
- **Dead-letter queue**: Failed messages for debugging
- **Scalability**: Multiple consumers can process messages in parallel

**Negative:**
- **Complexity**: Harder to debug than HTTP calls
- **Eventual consistency**: No immediate response
- **Message ordering**: Not guaranteed (need saga for complex workflows)
- **Operational overhead**: Need to monitor queues

**Risks:**
- Message duplication (need idempotent handlers)
- Queue backlog on high traffic

### Alternatives Considered

#### Alternative 1: Azure Service Bus

**Pros**: Managed, built-in features  
**Cons**: Vendor lock-in, cost

#### Alternative 2: Kafka

**Pros**: High throughput, event sourcing  
**Cons**: Over-engineered for our use case, operational complexity

#### Alternative 3: HTTP webhooks

**Pros**: Simple  
**Cons**: No reliability, coupling, retry logic complexity

### References

- [RabbitMQ](https://www.rabbitmq.com/)
- [MassTransit](https://masstransit.io/)
- [Enterprise Integration Patterns](https://www.enterpriseintegrationpatterns.com/)

---

## ADR-006: Use JWT for Authentication

**Status**: Accepted  
**Date**: 2024-01-12

### Context

Need to authenticate users across multiple services.

**Requirements**:
- Stateless (no session storage)
- Works with SPA (React)
- Contains user claims (id, roles)
- Short-lived access tokens

### Decision

Use **JWT (JSON Web Token)** with:
- **Access token**: 15 minutes lifetime, contains user claims
- **Refresh token**: 7 days lifetime, stored in Redis
- **Signing algorithm**: HMAC-SHA256

**Flow**:
1. User logs in → receives access + refresh tokens
2. Access token sent in `Authorization: Bearer <token>` header
3. When access token expires → use refresh token to get new access token
4. Logout → revoke refresh token

### Consequences

**Positive:**
- **Stateless**: No server-side sessions
- **Scalable**: Works with multiple API instances
- **Standard**: Widely supported (RFC 7519)
- **Contains claims**: No database lookup on each request

**Negative:**
- **Cannot revoke**: Access tokens valid until expiry (mitigated with short lifetime)
- **Token size**: Larger than session IDs
- **Secret management**: Need to secure signing key

**Risks:**
- Token leakage (XSS attacks)
- Refresh token theft

**Mitigations**:
- Store tokens in memory (not localStorage)
- HTTP-only cookies for refresh tokens (future improvement)
- Rotate refresh tokens on each use

### Alternatives Considered

#### Alternative 1: Session-based authentication

**Pros**: Can revoke immediately  
**Cons**: Requires shared session storage (Redis), less scalable

#### Alternative 2: OAuth2 with external provider

**Pros**: Offload auth to Google/Auth0  
**Cons**: Vendor dependency, less control

### References

- [JWT RFC 7519](https://datatracker.ietf.org/doc/html/rfc7519)
- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)

---

## ADR-007: Use CQRS Pattern

**Status**: Accepted  
**Date**: 2024-01-13

### Context

Services have complex business logic with different read/write requirements.

**Example**: Order creation (write) is complex, but viewing orders (read) is simple.

### Decision

Use **CQRS (Command Query Responsibility Segregation)** with **MediatR**:

- **Commands**: Write operations (CreateOrder, UpdateProduct)
- **Queries**: Read operations (GetProducts, GetOrderById)

**No separate read/write databases** (CQRS Lite), same database for both.

### Consequences

**Positive:**
- **Separation of concerns**: Commands and queries have different models
- **Optimized reads**: Can use simple DTOs, no domain logic
- **Testability**: Easy to test commands/queries independently
- **Scalability**: Can cache query results aggressively

**Negative:**
- **More code**: Separate handlers for each operation
- **Learning curve**: Team needs to understand pattern
- **Complexity**: Might be over-engineering for simple CRUDs

**Risks:**
- Team applying CQRS everywhere (even simple lookups)

**Guidelines**:
- Use CQRS for complex domains (Ordering, Payment)
- Skip CQRS for simple CRUD (Categories)

### Alternatives Considered

#### Alternative 1: Traditional Repository Pattern

**Pros**: Simple, widely understood  
**Cons**: Mixes read/write concerns

#### Alternative 2: Full CQRS with Event Sourcing

**Pros**: Complete audit trail, time-travel  
**Cons**: Huge complexity, overkill for MVP

### References

- [CQRS by Martin Fowler](https://martinfowler.com/bliki/CQRS.html)
- [MediatR](https://github.com/jbogard/MediatR)

---

## ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [001](#adr-001-use-microservices-architecture) | Use Microservices Architecture | Accepted | 2024-01-10 |
| [002](#adr-002-use-clean-architecture-for-services) | Use Clean Architecture | Accepted | 2024-01-10 |
| [003](#adr-003-use-postgresql-for-primary-database) | Use PostgreSQL | Accepted | 2024-01-11 |
| [004](#adr-004-use-redis-for-caching-and-session-storage) | Use Redis | Accepted | 2024-01-11 |
| [005](#adr-005-use-rabbitmq-for-async-communication) | Use RabbitMQ | Accepted | 2024-01-12 |
| [006](#adr-006-use-jwt-for-authentication) | Use JWT | Accepted | 2024-01-12 |
| [007](#adr-007-use-cqrs-pattern) | Use CQRS | Accepted | 2024-01-13 |

---

## Future ADRs (To Be Created)

- ADR-008: Choose Kubernetes Orchestration
- ADR-009: Monitoring Stack (Prometheus + Grafana)
- ADR-010: Logging Strategy (Seq vs ELK)
- ADR-011: API Versioning Strategy
- ADR-012: Frontend State Management (Zustand vs Redux)
- ADR-013: Deployment Strategy (Blue-Green vs Canary)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
