# 🚀 Локальне налаштування

Інструкції по запуску проекту на локальній машині для розробки.

---

## Передумови

Переконайтеся що встановлено все необхідне ПЗ:

- ✅ [.NET 9 SDK](prerequisites.md#1-net-sdk-90)
- ✅ [Docker Desktop](prerequisites.md#2-docker-desktop)
- ✅ [Git](prerequisites.md#3-git)
- ✅ [Node.js 20+](prerequisites.md#5-nodejs-для-frontend)
- ✅ IDE ([VS Code](prerequisites.md#option-3-visual-studio-code-cross-platform) / [Visual Studio](prerequisites.md#option-1-visual-studio-2022-windows) / [Rider](prerequisites.md#option-2-jetbrains-rider-cross-platform))

---

## Крок 1: Клонування репозиторію

### Через HTTPS
```bash
git clone https://github.com/your-username/eshop-microservices.git
cd eshop-microservices
```

### Через SSH (якщо налаштований SSH key)
```bash
git clone git@github.com:your-username/eshop-microservices.git
cd eshop-microservices
```

---

## Крок 2: Запуск інфраструктури (Docker)

### Варіант A: Docker Compose (Рекомендовано) ⭐

Запустити всю інфраструктуру однією командою:

```bash
cd deploy/docker
docker compose up -d
```

Це запустить:
- ✅ PostgreSQL (port 5432)
- ✅ Redis (port 6379)
- ✅ RabbitMQ (ports 5672, 15672)
- ✅ Seq (port 5341)
- ✅ Prometheus (port 9090) - опційно
- ✅ Grafana (port 3000) - опційно
- ✅ Jaeger (port 16686) - опційно

**Перевірка статусу**:
```bash
docker compose ps
```

**Зупинити інфраструктуру**:
```bash
docker compose down
```

**Зупинити та видалити volumes (дані)**:
```bash
docker compose down -v
```

---

### Варіант B: Окремо кожен сервіс (не рекомендовано)

Якщо хочете запустити вибірково:

```bash
# Тільки PostgreSQL
docker run -d \
  --name eshop-postgres \
  -e POSTGRES_USER=eshop \
  -e POSTGRES_PASSWORD=eshop123 \
  -p 5432:5432 \
  postgres:16-alpine

# Тільки Redis
docker run -d \
  --name eshop-redis \
  -p 6379:6379 \
  redis:7-alpine

# Тільки RabbitMQ
docker run -d \
  --name eshop-rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3-management-alpine
```

---

## Крок 3: Перевірка інфраструктури

### PostgreSQL
```bash
# Підключення через psql (якщо встановлений)
psql -h localhost -U eshop -d postgres

# АБО через Docker
docker exec -it postgres psql -U eshop

# Перевірка
postgres=# \l  # Список баз даних
postgres=# \q  # Вихід
```

### Redis
```bash
# Підключення через redis-cli
docker exec -it redis redis-cli

# Перевірка
127.0.0.1:6379> PING
PONG
127.0.0.1:6379> exit
```

### RabbitMQ Management UI
Відкрийте браузер: http://localhost:15672

**Login**: `guest`  
**Password**: `guest`

### Seq (Logs)
Відкрийте браузер: http://localhost:5341

---

## Крок 4: Запуск Backend сервісів

### Варіант A: Через IDE (Visual Studio / Rider)

1. Відкрийте `EShop.sln`
2. Виберіть "Multiple Startup Projects"
3. Поставте галочки на:
   - ✅ EShop.Identity.API
   - ✅ EShop.Catalog.API
   - ✅ EShop.Basket.API
   - ✅ EShop.Ordering.API
   - ✅ EShop.Payment.API
   - ✅ EShop.Notification.API
   - ✅ EShop.ApiGateway
4. Натисніть F5 (Start Debugging)

---

### Варіант B: Через командний рядок (dotnet run)

Відкрийте 7 окремих терміналів:

**Terminal 1: Identity Service**
```bash
cd src/Services/Identity/EShop.Identity.API
dotnet run
# Запуститься на http://localhost:5101
```

**Terminal 2: Catalog Service**
```bash
cd src/Services/Catalog/EShop.Catalog.API
dotnet run
# Запуститься на http://localhost:5102
```

**Terminal 3: Basket Service**
```bash
cd src/Services/Basket/EShop.Basket.API
dotnet run
# Запуститься на http://localhost:5103
```

**Terminal 4: Ordering Service**
```bash
cd src/Services/Ordering/EShop.Ordering.API
dotnet run
# Запуститься на http://localhost:5104
```

**Terminal 5: Payment Service**
```bash
cd src/Services/Payment/EShop.Payment.API
dotnet run
# Запуститься на http://localhost:5105
```

**Terminal 6: Notification Service**
```bash
cd src/Services/Notification/EShop.Notification.API
dotnet run
# Запуститься на http://localhost:5106
```

**Terminal 7: API Gateway**
```bash
cd src/ApiGateway/EShop.ApiGateway
dotnet run
# Запуститься на http://localhost:5000
```

---

### Варіант C: Через Docker Compose (всі сервіси) ⭐ Best для production-like

```bash
cd deploy/docker
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d
```

Це запустить всю інфраструктуру + всі сервіси разом.

---

## Крок 5: Запуск Frontend (React SPA)

```bash
cd src/Web/eshop-spa

# Встановити залежності (перший раз)
npm install

# Запустити dev server
npm run dev

# Відкриється на http://localhost:3000
```

---

## Крок 6: Перевірка що все працює

### Health Checks

Перевірте health endpoints:

```bash
# API Gateway
curl http://localhost:5000/health
# Має повернути: Healthy

# Identity Service
curl http://localhost:5101/health

# Catalog Service
curl http://localhost:5102/health

# І так далі для всіх сервісів...
```

### Swagger / Scalar UI

Відкрийте у браузері:

- **API Gateway Swagger**: http://localhost:5000/scalar/v1
- **Identity Service**: http://localhost:5101/scalar/v1
- **Catalog Service**: http://localhost:5102/scalar/v1

### Test endpoints

```bash
# Get products (без автентифікації)
curl http://localhost:5000/api/v1/products

# Register user
curl -X POST http://localhost:5000/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@test.com",
    "password": "Test123!",
    "firstName": "John",
    "lastName": "Doe"
  }'
```

---

## Крок 7: Seed data (Опційно)

Якщо хочете заповнити БД тестовими даними:

```bash
# Через Entity Framework migrations
cd src/Services/Catalog/EShop.Catalog.API
dotnet ef database update

# АБО через SQL script
docker exec -i postgres psql -U eshop -d catalog < deploy/scripts/seed-catalog-data.sql
```

---

## Типові проблеми та рішення

### Проблема 1: Port already in use

**Помилка**:
```
Error: listen EADDRINUSE: address already in use :::5432
```

**Рішення**:
```bash
# Знайти процес що використовує порт
# Windows
netstat -ano | findstr :5432
taskkill /PID <PID> /F

# macOS/Linux
lsof -i :5432
kill -9 <PID>

# АБО змініть порт у docker-compose.yml
```

---

### Проблема 2: Docker container не запускається

**Рішення**:
```bash
# Перегляд логів
docker compose logs postgres
docker compose logs redis

# Перезапуск конкретного контейнера
docker compose restart postgres

# Повний restart
docker compose down
docker compose up -d
```

---

### Проблема 3: Database connection error

**Помилка**:
```
Unable to connect to PostgreSQL
```

**Рішення**:
1. Перевірте що PostgreSQL запущений:
   ```bash
   docker ps | grep postgres
   ```

2. Перевірте connection string у `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Port=5432;Database=catalog;Username=eshop;Password=eshop123"
     }
   }
   ```

3. Якщо запускаєте у Docker - використовуйте `host.docker.internal` замість `localhost`

---

### Проблема 4: npm install fails

**Рішення**:
```bash
# Очистити npm cache
npm cache clean --force

# Видалити node_modules
rm -rf node_modules package-lock.json

# Переінсталювати
npm install
```

---

### Проблема 5: .NET SDK not found

**Помилка**:
```
The specified SDK 'Microsoft.NET.Sdk.Web' was not found
```

**Рішення**:
```bash
# Перевірте версію SDK
dotnet --list-sdks

# Якщо 9.0 немає - встановіть:
# https://dotnet.microsoft.com/download/dotnet/9.0

# Або змініть target framework у .csproj:
<TargetFramework>net8.0</TargetFramework>
```

---

## Hot Reload під час розробки

### Backend (.NET)
```bash
# Hot reload включений за замовчуванням у .NET 9
# Просто редагуйте код - зміни застосуються автоматично

# Якщо не працює:
dotnet watch run
```

### Frontend (React)
```bash
# Vite має вбудований hot reload
# Просто збережіть файл - сторінка оновиться
```

---

## Корисні команди для розробки

### Backend
```bash
# Restore NuGet packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Format code
dotnet format

# Database migrations
dotnet ef migrations add MigrationName -p Infrastructure -s API
dotnet ef database update -p Infrastructure -s API
```

### Frontend
```bash
# Run dev server
npm run dev

# Build for production
npm run build

# Preview production build
npm run preview

# Lint code
npm run lint

# Format code
npm run format
```

### Docker
```bash
# View logs
docker compose logs -f [service_name]

# Restart service
docker compose restart [service_name]

# Execute command in container
docker compose exec postgres psql -U eshop

# View resource usage
docker stats
```

---

## Environment Variables

### Backend (.NET)

**Development** (appsettings.Development.json):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=catalog;Username=eshop;Password=eshop123"
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  }
}
```

**Production** (Environment variables):
```bash
export ConnectionStrings__DefaultConnection="Host=postgres;Database=catalog;Username=eshop;Password=<secure-password>"
export Redis__ConnectionString="redis:6379"
export RabbitMQ__Host="rabbitmq"
```

### Frontend (React)

Створіть `.env.local`:
```env
VITE_API_URL=http://localhost:5000
VITE_ENABLE_MOCK_API=false
```

---

## Наступні кроки

Після успішного запуску:

1. ✅ Ознайомтеся з [Team Agreement](team-agreement.md)
2. ✅ Почитайте [API Documentation](../../04-services/)
3. ✅ Подивіться [Implementation Plan](../../07-implementation-plan/)
4. ✅ Візьміть першу задачу з [GitHub Projects](../../06-development-workflow/task-management.md)

---

## Додаткові ресурси

- [Docker Setup Guide](docker-setup.md) - Детальніше про Docker
- [Troubleshooting Guide](../../11-troubleshooting/common-issues.md) - Типові проблеми
- [Debugging Guide](../../11-troubleshooting/debugging-guide.md) - Як дебажити

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15

**🎉 Вітаємо! Тепер ви готові до розробки!**
