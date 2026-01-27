# 🐳 Docker Setup Guide

Детальний гайд по налаштуванню та використанню Docker для E-Shop Microservices.

---

## Передумови

- ✅ Docker Desktop встановлений ([Інструкції](prerequisites.md#2-docker-desktop))
- ✅ Docker Compose включений (зазвичай йде разом з Docker Desktop)
- ✅ Мінімум 8GB RAM виділено для Docker
- ✅ Мінімум 50GB вільного місця на диску

---

## Структура Docker файлів

```
eshop-microservices/
├── deploy/
│   └── docker/
│       ├── docker-compose.yml                 # Infrastructure services
│       ├── docker-compose.services.yml        # Application services
│       ├── docker-compose.override.yml        # Development overrides
│       └── .env.example                       # Environment variables template
│
└── src/
    └── Services/
        ├── Identity/
        │   └── EShop.Identity.API/
        │       └── Dockerfile                 # Identity service image
        ├── Catalog/
        │   └── EShop.Catalog.API/
        │       └── Dockerfile                 # Catalog service image
        └── ... (інші сервіси)
```

---

## docker-compose.yml (Infrastructure)

Основний файл для інфраструктурних сервісів:

```yaml
version: '3.8'

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
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U eshop"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - eshop-network

  # Redis Cache
  redis:
    image: redis:7-alpine
    container_name: eshop-redis
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
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
      - "5672:5672"    # AMQP
      - "15672:15672"  # Management UI
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - eshop-network

  # Seq Logging
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

  # Prometheus (Optional)
  prometheus:
    image: prom/prometheus:latest
    container_name: eshop-prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
    networks:
      - eshop-network

  # Grafana (Optional)
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

  # Jaeger Tracing (Optional)
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: eshop-jaeger
    ports:
      - "5775:5775/udp"
      - "6831:6831/udp"
      - "6832:6832/udp"
      - "5778:5778"
      - "16686:16686"  # UI
      - "14268:14268"
      - "14250:14250"
      - "9411:9411"
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

---

## docker-compose.services.yml (Application)

Файл для запуску всіх мікросервісів:

```yaml
version: '3.8'

services:
  # Identity Service
  identity-api:
    build:
      context: ../../
      dockerfile: src/Services/Identity/EShop.Identity.API/Dockerfile
    container_name: eshop-identity-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=identity;Username=eshop;Password=eshop123
      - JwtSettings__Secret=ThisIsAVerySecureSecretKeyForJWT12345
      - JwtSettings__Issuer=EShop.Identity
      - JwtSettings__Audience=EShop.Clients
    ports:
      - "5101:80"
    depends_on:
      postgres:
        condition: service_healthy
      seq:
        condition: service_started
    networks:
      - eshop-network

  # Catalog Service
  catalog-api:
    build:
      context: ../../
      dockerfile: src/Services/Catalog/EShop.Catalog.API/Dockerfile
    container_name: eshop-catalog-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=catalog;Username=eshop;Password=eshop123
      - Redis__ConnectionString=redis:6379
    ports:
      - "5102:80"
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    networks:
      - eshop-network

  # Basket Service
  basket-api:
    build:
      context: ../../
      dockerfile: src/Services/Basket/EShop.Basket.API/Dockerfile
    container_name: eshop-basket-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - Redis__ConnectionString=redis:6379
      - RabbitMQ__Host=rabbitmq
    ports:
      - "5103:80"
    depends_on:
      redis:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    networks:
      - eshop-network

  # Ordering Service
  ordering-api:
    build:
      context: ../../
      dockerfile: src/Services/Ordering/EShop.Ordering.API/Dockerfile
    container_name: eshop-ordering-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=ordering;Username=eshop;Password=eshop123
      - RabbitMQ__Host=rabbitmq
    ports:
      - "5104:80"
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
    networks:
      - eshop-network

  # Payment Service
  payment-api:
    build:
      context: ../../
      dockerfile: src/Services/Payment/EShop.Payment.API/Dockerfile
    container_name: eshop-payment-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - RabbitMQ__Host=rabbitmq
    ports:
      - "5105:80"
    depends_on:
      rabbitmq:
        condition: service_healthy
    networks:
      - eshop-network

  # Notification Service
  notification-api:
    build:
      context: ../../
      dockerfile: src/Services/Notification/EShop.Notification.API/Dockerfile
    container_name: eshop-notification-api
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - RabbitMQ__Host=rabbitmq
      - Smtp__Host=smtp.mailtrap.io
      - Smtp__Port=587
    ports:
      - "5106:80"
    depends_on:
      rabbitmq:
        condition: service_healthy
    networks:
      - eshop-network

  # API Gateway
  api-gateway:
    build:
      context: ../../
      dockerfile: src/ApiGateway/EShop.ApiGateway/Dockerfile
    container_name: eshop-api-gateway
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
    ports:
      - "5000:80"
    depends_on:
      - identity-api
      - catalog-api
      - basket-api
      - ordering-api
    networks:
      - eshop-network

networks:
  eshop-network:
    external: true
```

---

## Dockerfile для .NET сервісів

Приклад Dockerfile (однаковий для всіх API):

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution file
COPY ["EShop.sln", "./"]

# Copy all project files (для restore)
COPY ["src/Services/Catalog/EShop.Catalog.API/EShop.Catalog.API.csproj", "src/Services/Catalog/EShop.Catalog.API/"]
COPY ["src/Services/Catalog/EShop.Catalog.Application/EShop.Catalog.Application.csproj", "src/Services/Catalog/EShop.Catalog.Application/"]
COPY ["src/Services/Catalog/EShop.Catalog.Domain/EShop.Catalog.Domain.csproj", "src/Services/Catalog/EShop.Catalog.Domain/"]
COPY ["src/Services/Catalog/EShop.Catalog.Infrastructure/EShop.Catalog.Infrastructure.csproj", "src/Services/Catalog/EShop.Catalog.Infrastructure/"]
COPY ["src/BuildingBlocks/", "src/BuildingBlocks/"]

# Restore dependencies
RUN dotnet restore "src/Services/Catalog/EShop.Catalog.API/EShop.Catalog.API.csproj"

# Copy everything else
COPY . .

# Build
WORKDIR "/src/src/Services/Catalog/EShop.Catalog.API"
RUN dotnet build "EShop.Catalog.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "EShop.Catalog.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Security: Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy published files
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1

EXPOSE 80

ENTRYPOINT ["dotnet", "EShop.Catalog.API.dll"]
```

---

## Команди Docker Compose

### Базові команди

```bash
# Запустити всю інфраструктуру
docker compose up -d

# Запустити з логами (foreground)
docker compose up

# Зупинити всі сервіси
docker compose down

# Зупинити та видалити volumes
docker compose down -v

# Перезапустити конкретний сервіс
docker compose restart postgres

# Подивитися статус
docker compose ps

# Подивитися логи
docker compose logs -f

# Логи конкретного сервісу
docker compose logs -f postgres
```

### Запуск інфраструктури + сервісів

```bash
# Тільки інфраструктура
docker compose -f docker-compose.yml up -d

# Інфраструктура + всі сервіси
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d

# Build and up (після змін коду)
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --build

# Rebuild конкретного сервісу
docker compose -f docker-compose.services.yml up -d --build catalog-api
```

---

## Environment Variables (.env файл)

Створіть файл `deploy/docker/.env`:

```env
# PostgreSQL
POSTGRES_USER=eshop
POSTGRES_PASSWORD=eshop123
POSTGRES_DB=postgres

# Redis
REDIS_PASSWORD=

# RabbitMQ
RABBITMQ_DEFAULT_USER=guest
RABBITMQ_DEFAULT_PASS=guest

# JWT
JWT_SECRET=ThisIsAVerySecureSecretKeyForJWT12345
JWT_ISSUER=EShop.Identity
JWT_AUDIENCE=EShop.Clients

# Seq
SEQ_API_KEY=

# Grafana
GF_SECURITY_ADMIN_USER=admin
GF_SECURITY_ADMIN_PASSWORD=admin
```

Використання у docker-compose.yml:

```yaml
services:
  postgres:
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
```

---

## Налаштування Docker Desktop

### Windows

1. **WSL 2 Backend** (рекомендовано):
   ```powershell
   wsl --install
   wsl --set-default-version 2
   ```

2. **Resources налаштування**:
   - Settings → Resources → Advanced
   - CPUs: 4+ cores
   - Memory: 8GB+
   - Swap: 2GB
   - Disk: 50GB+

3. **File Sharing**:
   - Settings → Resources → File Sharing
   - Додайте `C:\Users\YourName\Projects`

### macOS

1. **Resources**:
   - Preferences → Resources
   - CPUs: 4+
   - Memory: 8GB+
   - Swap: 2GB
   - Disk: 50GB+

2. **File Sharing**:
   - Автоматично працює для `/Users`, `/Volumes`, `/tmp`

### Linux

Docker встановлюється нативно, ніяких додаткових налаштувань не потрібно.

---

## Docker Networks

### Створення custom network

```bash
# Створити bridge network
docker network create eshop-network

# Переглянути networks
docker network ls

# Inspect network
docker network inspect eshop-network

# Видалити network
docker network rm eshop-network
```

### Підключення контейнерів

```yaml
services:
  postgres:
    networks:
      - eshop-network

  redis:
    networks:
      - eshop-network
```

### Комунікація між контейнерами

Всередині Docker network контейнери бачать один одного по імені:

```csharp
// ❌ Не використовуйте localhost у Docker
"ConnectionString": "Host=localhost;Port=5432"

// ✅ Використовуйте назву сервісу
"ConnectionString": "Host=postgres;Port=5432"
```

---

## Docker Volumes

### Named volumes (рекомендовано)

```yaml
volumes:
  postgres_data:
  redis_data:

services:
  postgres:
    volumes:
      - postgres_data:/var/lib/postgresql/data
```

**Переваги**:
- Керуються Docker
- Кросплатформенні
- Можна backup/restore

### Bind mounts (для development)

```yaml
services:
  postgres:
    volumes:
      - ./data/postgres:/var/lib/postgresql/data
      - ./init-scripts:/docker-entrypoint-initdb.d
```

**Переваги**:
- Прямий доступ до файлів
- Швидше редагування

### Команди для volumes

```bash
# Список volumes
docker volume ls

# Inspect volume
docker volume inspect postgres_data

# Backup volume
docker run --rm -v postgres_data:/data -v $(pwd):/backup alpine tar czf /backup/postgres_backup.tar.gz /data

# Restore volume
docker run --rm -v postgres_data:/data -v $(pwd):/backup alpine tar xzf /backup/postgres_backup.tar.gz -C /

# Видалити volume
docker volume rm postgres_data

# Видалити всі невикористані volumes
docker volume prune
```

---

## Health Checks

### У docker-compose.yml

```yaml
services:
  postgres:
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U eshop"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 30s
```

### У Dockerfile

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/health || exit 1
```

### Перевірка health status

```bash
# Статус всіх контейнерів
docker compose ps

# Inspect конкретного контейнера
docker inspect eshop-postgres | grep -A 10 Health

# Логи healthcheck
docker inspect eshop-postgres | grep -A 20 Healthcheck
```

---

## Troubleshooting

### Проблема 1: Cannot connect to Docker daemon

**Помилка**:
```
Cannot connect to the Docker daemon at unix:///var/run/docker.sock
```

**Рішення**:
```bash
# Перевірте чи запущений Docker Desktop
# Windows/macOS: Відкрийте Docker Desktop

# Linux: Запустіть daemon
sudo systemctl start docker
sudo systemctl enable docker

# Додайте користувача до групи docker (Linux)
sudo usermod -aG docker $USER
newgrp docker
```

---

### Проблема 2: Port already in use

**Помилка**:
```
Error: Bind for 0.0.0.0:5432 failed: port is already allocated
```

**Рішення**:
```bash
# Знайдіть процес
# Windows
netstat -ano | findstr :5432
taskkill /PID <PID> /F

# macOS/Linux
lsof -i :5432
kill -9 <PID>

# АБО змініть порт у docker-compose.yml
ports:
  - "5433:5432"  # External:Internal
```

---

### Проблема 3: Container keeps restarting

**Рішення**:
```bash
# Подивіться логи
docker compose logs -f postgres

# Inspect контейнер
docker inspect eshop-postgres

# Спробуйте запустити інтерактивно
docker compose run --rm postgres sh
```

---

### Проблема 4: Out of disk space

**Помилка**:
```
no space left on device
```

**Рішення**:
```bash
# Очистити невикористані resources
docker system prune -a --volumes

# Видалити тільки stopped контейнери
docker container prune

# Видалити тільки unused images
docker image prune -a

# Видалити тільки unused volumes
docker volume prune
```

---

### Проблема 5: Slow performance (Windows/macOS)

**Рішення**:
1. **Збільшіть resources**:
   - Docker Desktop → Settings → Resources
   - Memory: 8GB+ (для всіх сервісів)

2. **Використовуйте named volumes** замість bind mounts:
   ```yaml
   # ❌ Slow
   - ./data:/data
   
   # ✅ Fast
   volumes:
     - postgres_data:/var/lib/postgresql/data
   ```

3. **Windows: Enable WSL 2**:
   - Docker Desktop → Settings → General
   - Use WSL 2 based engine

---

## Production Considerations

### Security

```yaml
# ❌ НЕ використовуйте у production
environment:
  POSTGRES_PASSWORD: eshop123

# ✅ Використовуйте secrets
secrets:
  postgres_password:
    external: true

services:
  postgres:
    secrets:
      - postgres_password
    environment:
      POSTGRES_PASSWORD_FILE: /run/secrets/postgres_password
```

### Multi-stage builds

```dockerfile
# Використовуйте multi-stage для меншого розміру image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# ... build steps

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# ... тільки runtime
```

### Resource limits

```yaml
services:
  catalog-api:
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 512M
        reservations:
          cpus: '0.25'
          memory: 256M
```

---

## Корисні команди

```bash
# Переглянути використання ресурсів
docker stats

# Очистити все (ОБЕРЕЖНО!)
docker system prune -a --volumes

# Експорт/імпорт image
docker save eshop-catalog-api > catalog-api.tar
docker load < catalog-api.tar

# Запустити команду всередині контейнера
docker exec -it eshop-postgres psql -U eshop

# Скопіювати файли з/в контейнер
docker cp ./backup.sql eshop-postgres:/backup.sql
docker cp eshop-postgres:/var/lib/postgresql/data ./data

# Подивитися layer history image
docker history eshop-catalog-api
```

---

## Наступні кроки

- ✅ [Kubernetes Deployment](../../09-deployment/kubernetes-deployment.md) - Для production
- ✅ [CI/CD Pipeline](../../09-deployment/ci-cd-pipeline.md) - Автоматизація
- ✅ [Monitoring](../../10-production-readiness/monitoring-alerts.md) - Observability

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
