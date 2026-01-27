# 🏗️ Phase 1: Foundation & Architecture Setup

**Duration**: 1-2 weeks  
**Team Size**: 2-3 developers  
**Status**: 📋 Planning

---

## Objectives

- ✅ Setup development environment
- ✅ Initialize solution structure (Clean Architecture)
- ✅ Configure CI/CD pipelines
- ✅ Setup shared infrastructure (Docker Compose)
- ✅ Create BuildingBlocks library
- ✅ Setup observability stack (Seq, Jaeger, Prometheus)

---

## Prerequisites

Before starting Phase 1:
- [ ] Team has read [Architecture Overview](../01-overview/architecture-diagram.md)
- [ ] Team has agreed on [Tech Stack](../01-overview/tech-stack.md)
- [ ] Development machines meet [Prerequisites](../02-getting-started/prerequisites.md)
- [ ] GitHub repository created
- [ ] Azure DevOps / GitHub Actions configured

---

## Tasks Breakdown

### 1.1 Repository & Solution Setup

**Estimated Time**: 2 days  
**Assigned To**: Lead Developer

**Tasks:**

1. **Initialize Git Repository**
   ```bash
   mkdir eshop-microservices
   cd eshop-microservices
   git init
   git remote add origin https://github.com/yourorg/eshop-microservices.git
   ```

2. **Create Solution Structure**
   ```bash
   dotnet new sln -n EShop
   
   # Create directories
   mkdir -p src/Services/Identity
   mkdir -p src/Services/Catalog
   mkdir -p src/Services/Basket
   mkdir -p src/Services/Ordering
   mkdir -p src/Services/Payment
   mkdir -p src/Services/Notification
   mkdir -p src/ApiGateways
   mkdir -p src/BuildingBlocks
   mkdir -p src/WebApps
   mkdir -p tests
   mkdir -p deploy
   mkdir -p docs
   ```

3. **Setup .gitignore**
   ```gitignore
   ## Ignore Visual Studio temporary files
   .vs/
   *.user
   *.suo
   
   ## Ignore build results
   [Dd]ebug/
   [Rr]elease/
   bin/
   obj/
   
   ## Ignore packages
   packages/
   *.nupkg
   
   ## Ignore user-specific files
   *.rsuser
   *.suo
   *.userosscache
   
   ## Ignore secrets
   appsettings.Development.json
   appsettings.*.json
   !appsettings.json
   secrets.json
   
   ## Ignore Docker volumes
   docker-volumes/
   
   ## Ignore logs
   logs/
   *.log
   ```

4. **Create EditorConfig**
   ```ini
   # .editorconfig
   root = true
   
   [*]
   charset = utf-8
   indent_style = space
   indent_size = 4
   end_of_line = lf
   insert_final_newline = true
   trim_trailing_whitespace = true
   
   [*.{json,yml,yaml}]
   indent_size = 2
   
   [*.md]
   trim_trailing_whitespace = false
   ```

**Deliverables:**
- ✅ Repository initialized with proper structure
- ✅ .gitignore, .editorconfig configured
- ✅ README.md with project overview

---

### 1.2 BuildingBlocks Library

**Estimated Time**: 3 days  
**Assigned To**: Backend Developer

**Create shared library for cross-cutting concerns:**

```bash
cd src/BuildingBlocks
dotnet new classlib -n EShop.BuildingBlocks.Domain
dotnet new classlib -n EShop.BuildingBlocks.Application
dotnet new classlib -n EShop.BuildingBlocks.Infrastructure
dotnet new classlib -n EShop.BuildingBlocks.Messaging
```

**1. Domain Layer**

```csharp
// src/BuildingBlocks/EShop.BuildingBlocks.Domain/AggregateRoot.cs

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();
    
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
    
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

// src/BuildingBlocks/EShop.BuildingBlocks.Domain/Entity.cs

public abstract class Entity
{
    public Guid Id { get; protected set; }
    public DateTime CreatedAt { get; protected set; }
    public string CreatedBy { get; protected set; } = string.Empty;
    public DateTime? ModifiedAt { get; protected set; }
    public string? ModifiedBy { get; protected set; }
    
    protected Entity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTime.UtcNow;
    }
}

// src/BuildingBlocks/EShop.BuildingBlocks.Domain/IDomainEvent.cs

public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}

// src/BuildingBlocks/EShop.BuildingBlocks.Domain/ValueObject.cs

public abstract class ValueObject
{
    protected abstract IEnumerable<object> GetEqualityComponents();
    
    public override bool Equals(object? obj)
    {
        if (obj == null || obj.GetType() != GetType())
            return false;
        
        var other = (ValueObject)obj;
        
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }
    
    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }
}
```

**2. Application Layer**

```csharp
// src/BuildingBlocks/EShop.BuildingBlocks.Application/Result.cs

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }
    
    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }
    
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);
}

// src/BuildingBlocks/EShop.BuildingBlocks.Application/Error.cs

public record Error(string Code, string Message)
{
    public static Error None = new(string.Empty, string.Empty);
    public static Error NullValue = new("Error.NullValue", "Null value was provided");
}

// src/BuildingBlocks/EShop.BuildingBlocks.Application/Exceptions/NotFoundException.cs

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    
    public NotFoundException(string name, object key)
        : base($"Entity '{name}' ({key}) was not found.") { }
}
```

**3. Infrastructure Layer**

```csharp
// src/BuildingBlocks/EShop.BuildingBlocks.Infrastructure/DistributedCacheExtensions.cs

public static class DistributedCacheExtensions
{
    public static async Task<T?> GetAsync<T>(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        var data = await cache.GetStringAsync(key, cancellationToken);
        
        if (string.IsNullOrEmpty(data))
            return default;
        
        return JsonSerializer.Deserialize<T>(data);
    }
    
    public static async Task SetAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();
        
        if (expiration.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiration;
        
        var json = JsonSerializer.Serialize(value);
        await cache.SetStringAsync(key, json, options, cancellationToken);
    }
}
```

**4. Messaging Library**

```csharp
// src/BuildingBlocks/EShop.BuildingBlocks.Messaging/IIntegrationEvent.cs

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredOn { get; }
}

// src/BuildingBlocks/EShop.BuildingBlocks.Messaging/IntegrationEvent.cs

public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
```

**Deliverables:**
- ✅ BuildingBlocks projects created
- ✅ Base classes: Entity, AggregateRoot, ValueObject
- ✅ Result pattern implementation
- ✅ Common exceptions
- ✅ Extension methods

---

### 1.3 Docker Infrastructure Setup

**Estimated Time**: 2 days  
**Assigned To**: DevOps Engineer

**Create docker-compose.yml for all infrastructure:**

```yaml
# deploy/docker/docker-compose.yml

version: '3.9'

services:
  # PostgreSQL Database
  postgres:
    image: postgres:16-alpine
    container_name: eshop-postgres
    environment:
      POSTGRES_USER: eshop
      POSTGRES_PASSWORD: eshop123
      POSTGRES_DB: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./init-scripts:/docker-entrypoint-initdb.d
    networks:
      - eshop-network

  # Redis Cache
  redis:
    image: redis:7-alpine
    container_name: eshop-redis
    command: redis-server --requirepass eshop123
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    networks:
      - eshop-network

  # RabbitMQ Message Broker
  rabbitmq:
    image: rabbitmq:3-management-alpine
    container_name: eshop-rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    ports:
      - "5672:5672"
      - "15672:15672"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - eshop-network

  # Seq (Logging)
  seq:
    image: datalust/seq:latest
    container_name: eshop-seq
    environment:
      ACCEPT_EULA: Y
    ports:
      - "5341:80"
    volumes:
      - seq_data:/data
    networks:
      - eshop-network

  # Jaeger (Distributed Tracing)
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: eshop-jaeger
    ports:
      - "5775:5775/udp"
      - "6831:6831/udp"
      - "16686:16686"
      - "14268:14268"
    networks:
      - eshop-network

  # Prometheus (Metrics)
  prometheus:
    image: prom/prometheus:latest
    container_name: eshop-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    networks:
      - eshop-network

  # Grafana (Dashboards)
  grafana:
    image: grafana/grafana:latest
    container_name: eshop-grafana
    ports:
      - "3001:3000"
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
    volumes:
      - grafana_data:/var/lib/grafana
    depends_on:
      - prometheus
    networks:
      - eshop-network

volumes:
  postgres_data:
  redis_data:
  rabbitmq_data:
  seq_data:
  prometheus_data:
  grafana_data:

networks:
  eshop-network:
    driver: bridge
```

**Database Initialization Script:**

```sql
-- deploy/docker/init-scripts/01-create-databases.sql

CREATE DATABASE identity;
CREATE DATABASE catalog;
CREATE DATABASE ordering;
CREATE DATABASE payment;

GRANT ALL PRIVILEGES ON DATABASE identity TO eshop;
GRANT ALL PRIVILEGES ON DATABASE catalog TO eshop;
GRANT ALL PRIVILEGES ON DATABASE ordering TO eshop;
GRANT ALL PRIVILEGES ON DATABASE payment TO eshop;
```

**Deliverables:**
- ✅ docker-compose.yml configured
- ✅ All infrastructure services running
- ✅ Database initialization scripts

---

### 1.4 CI/CD Pipeline Setup

**Estimated Time**: 2 days  
**Assigned To**: DevOps Engineer

**GitHub Actions Workflow:**

```yaml
# .github/workflows/ci.yml

name: CI/CD Pipeline

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

env:
  DOTNET_VERSION: '9.0.x'

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore --configuration Release
    
    - name: Run tests
      run: dotnet test --no-build --configuration Release --verbosity normal
    
    - name: Publish coverage
      uses: codecov/codecov-action@v3
      with:
        file: ./coverage.xml
        fail_ci_if_error: true

  code-quality:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Run SonarQube Scanner
      uses: sonarsource/sonarqube-scan-action@master
      env:
        SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        SONAR_HOST_URL: ${{ secrets.SONAR_HOST_URL }}

  docker-build:
    runs-on: ubuntu-latest
    needs: build
    if: github.ref == 'refs/heads/main'
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Login to Docker Hub
      uses: docker/login-action@v3
      with:
        username: ${{ secrets.DOCKER_USERNAME }}
        password: ${{ secrets.DOCKER_PASSWORD }}
    
    - name: Build and push Docker images
      run: |
        docker build -t yourorg/eshop-identity:${{ github.sha }} -f src/Services/Identity/Dockerfile .
        docker push yourorg/eshop-identity:${{ github.sha }}
```

**Deliverables:**
- ✅ GitHub Actions workflow configured
- ✅ Automated build and test
- ✅ Code quality checks (SonarQube)
- ✅ Docker image publishing

---

### 1.5 Development Guidelines & Documentation

**Estimated Time**: 1 day  
**Assigned To**: Team Lead

**Create documentation:**

1. **CONTRIBUTING.md**
2. **CODE_OF_CONDUCT.md**
3. **Architecture Decision Records (ADRs)**
4. **API Design Guidelines**

**Example ADR:**

```markdown
# ADR 001: Use Clean Architecture

**Status**: Accepted  
**Date**: 2024-01-15  
**Deciders**: Team Lead, Senior Developers

## Context

We need to choose an architectural pattern for our microservices.

## Decision

We will use Clean Architecture (Onion Architecture) for all services.

## Consequences

**Positive:**
- Clear separation of concerns
- Testability
- Independence from frameworks
- Flexibility

**Negative:**
- More boilerplate code
- Steeper learning curve for juniors
```

**Deliverables:**
- ✅ CONTRIBUTING.md
- ✅ Architecture Decision Records
- ✅ Coding standards documented

---

## Success Criteria

### Definition of Done

- [x] Solution structure created with all projects
- [x] BuildingBlocks library implemented
- [x] Docker Compose with all infrastructure running
- [x] CI/CD pipeline passing
- [x] Documentation complete
- [x] Team trained on architecture

### Quality Metrics

- **Code Coverage**: N/A (no business logic yet)
- **Build Time**: < 5 minutes
- **Pipeline Success Rate**: 100%

---

## Team Roles & Responsibilities

| Role | Responsibilities | Team Member |
|------|------------------|-------------|
| **Lead Developer** | Architecture decisions, code reviews | TBD |
| **Backend Developer** | BuildingBlocks implementation | TBD |
| **DevOps Engineer** | CI/CD, Docker setup | TBD |

---

## Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Team unfamiliar with Clean Architecture | High | Medium | Training sessions, pair programming |
| Docker environment issues | Medium | Low | Detailed setup docs, troubleshooting guide |
| CI/CD pipeline failures | Medium | Low | Gradual rollout, manual fallback |

---

## Timeline

```
Week 1:
├── Day 1-2: Repository & Solution Setup
├── Day 3-5: BuildingBlocks Library
└── Review & Documentation

Week 2:
├── Day 1-2: Docker Infrastructure
├── Day 3-4: CI/CD Pipeline
└── Day 5: Final review & handoff to Phase 2
```

---

## Deliverables Checklist

- [ ] Git repository initialized
- [ ] Solution structure with 4 layers per service
- [ ] BuildingBlocks library with base classes
- [ ] Docker Compose running all infrastructure
- [ ] CI/CD pipeline green
- [ ] README.md with setup instructions
- [ ] Architecture Decision Records
- [ ] Team trained and ready for Phase 2

---

## Next Phase

→ [Phase 2: Identity Service Implementation](phase-2-identity.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
