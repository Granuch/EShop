# Validation в EShop

## 🎯 Подход к валидации

### ✅ Правильный подход (Result Pattern)

ValidationBehavior **НЕ бросает exceptions** для валидации. Вместо этого:

1. Проверяет что команда возвращает `Result<T>`
2. Если валидация не прошла → возвращает `Result<T>.Failure(error)`
3. Контроллер проверяет `result.IsFailure` и возвращает 400

**Преимущества:**
- ✅ Не захламляет логи "ошибками"
- ✅ Валидация — это ожидаемый flow, а не exception
- ✅ Лучше производительность (нет stack trace)
- ✅ Чище код

### ❌ Старый подход (Exceptions)

ValidationBehavior бросал `ValidationException` → попадал в GlobalExceptionHandler → логировался как ERROR.

**Проблемы:**
- ❌ Логи захламлены ValidationException
- ❌ Нет различия между реальными ошибками и валидацией
- ❌ Хуже производительность

---

## 📝 Как использовать

### 1. Command/Query должен возвращать `Result<T>`

```csharp
public record RegisterCommand : IRequest<Result<RegisterResponse>>
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
```

### 2. Создать FluentValidation Validator

```csharp
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();
            
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8);
    }
}
```

### 3. ValidationBehavior автоматически проверяет

Если валидация не пройдена → возвращает `Result<T>.Failure(error)`

### 4. Контроллер проверяет Result

```csharp
[HttpPost("register")]
public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterCommand command)
{
    var result = await _mediator.Send(command);

    if (result.IsFailure)
    {
        return BadRequest(new { error = result.Error!.Code, message = result.Error.Message });
    }

    return Ok(result.Value);
}
```

---

## 🔍 Как это работает

### Flow для валидной команды:
```
Request → ValidationBehavior → Validator (OK) → Handler → Result.Success → 200 OK
```

### Flow для невалидной команды:
```
Request → ValidationBehavior → Validator (FAIL) → Result.Failure → 400 Bad Request
```

**НЕТ exceptions!**

---

## 📊 Логи

### Валидация провалена (Information)
```log
[Information] Validation failed for request. TraceId: abc123, Errors: {"Email":["Email is required"]}
```

### Реальная ошибка (Error)
```log
[Error] Unhandled exception occurred. TraceId: abc123
System.NullReferenceException...
```

---

## 🧪 Тестирование

```csharp
[Test]
public async Task Handle_InvalidRequest_ReturnsFailureResult()
{
    // Arrange
    var command = new RegisterCommand { Email = "invalid" };

    // Act
    var result = await _mediator.Send(command);

    // Assert
    Assert.That(result.IsFailure, Is.True);
    Assert.That(result.Error!.Code, Is.EqualTo("Validation.Failed"));
}
```

---

## ⚙️ Конфигурация

ValidationBehavior автоматически регистрируется в `AddIdentityApplication()`:

```csharp
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

Все validators регистрируются через:

```csharp
services.AddValidatorsFromAssembly(assembly);
```

---

## 🎨 Best Practices

1. **Всегда используй `Result<T>` для команд**
2. **Валидируй на уровне Application (FluentValidation)**
3. **Бизнес-правила в Domain (DomainException)**
4. **ValidationException только для fallback**
5. **Логируй валидацию как Information, не Error**
