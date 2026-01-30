# EShop Observability Stack

Документация по настройке и использованию observability инструментов для EShop.

## Компоненты

### 1. Seq - Централизованное логирование
- **URL**: http://localhost:5341
- **Описание**: Сервер для сбора и анализа структурированных логов

### 2. Prometheus - Сбор метрик
- **URL**: http://localhost:9090
- **Описание**: Time-series база данных для метрик

### 3. Grafana - Визуализация
- **URL**: http://localhost:3000
- **Credentials**: admin / admin
- **Описание**: Дашборды и алерты

## Быстрый старт

```bash
# Запустить observability stack
docker-compose -f docker-compose.observability.yml up -d

# Проверить статус
docker-compose -f docker-compose.observability.yml ps

# Остановить
docker-compose -f docker-compose.observability.yml down
```

## Identity Service Endpoints

### Health Checks
- `/health` - Полный health check с детальным ответом
- `/health/ready` - Проверка готовности (database, roles)
- `/health/live` - Проверка жизнеспособности

### Metrics
- `/metrics` - Prometheus metrics endpoint

## Метрики Identity Service

### Аутентификация
- `identity_login_attempts_total` - Попытки входа (status: success/failure/2fa_required, reason)
- `identity_login_duration_seconds` - Длительность операции входа

### Регистрация
- `identity_registrations_total` - Регистрации пользователей (status)

### Токены
- `identity_token_refresh_total` - Обновления токенов
- `identity_token_revocations_total` - Отзывы токенов
- `identity_token_generation_duration_seconds` - Время генерации токенов

### Пароли
- `identity_password_operations_total` - Операции с паролями (operation: change/reset/forgot)

### 2FA
- `identity_2fa_operations_total` - Операции 2FA (operation: enable/verify/disable)

### Email
- `identity_email_operations_total` - Операции с email (operation: confirm)

## Structured Logging

Все логи обогащены следующими свойствами:
- `Application` - Имя приложения
- `MachineName` - Имя хоста
- `ThreadId` - ID потока
- `EnvironmentName` - Окружение (Development/Production)

### Примеры запросов в Seq

```sql
-- Неудачные попытки входа
Application = 'EShop.Identity.API' and @Level = 'Warning' and @Message like '%Login attempt failed%'

-- Все операции пользователя
UserId = 'user-guid-here'

-- Ошибки за последний час
@Level = 'Error' and @Timestamp > Now() - 1h
```

## Grafana Dashboards

### EShop Identity Service Dashboard
- Успешные/неудачные входы (24ч)
- Новые регистрации (24ч)
- Rate входов
- Latency входов (p50, p95, p99)
- HTTP request rate
- HTTP response time
- Password operations
- 2FA operations

## Конфигурация

### appsettings.json

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  }
}
```

### Prometheus scrape config

```yaml
scrape_configs:
  - job_name: 'identity-service'
    metrics_path: '/metrics'
    static_configs:
      - targets: ['host.docker.internal:5001']
```

## Troubleshooting

### Seq не получает логи
1. Проверьте, что Seq запущен: `docker ps | grep seq`
2. Проверьте URL в appsettings.json
3. Проверьте firewall

### Prometheus не скрапит метрики
1. Откройте http://localhost:9090/targets
2. Проверьте, что Identity service доступен
3. Проверьте `/metrics` endpoint напрямую

### Grafana не показывает данные
1. Проверьте подключение к Prometheus в Grafana
2. Убедитесь, что метрики существуют в Prometheus
3. Проверьте временной диапазон в дашборде
