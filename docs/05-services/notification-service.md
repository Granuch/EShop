# 📧 Notification Service

Сервіс відправки email/SMS нотифікацій з підтримкою templates та queuing.

---

## Огляд

Notification Service відповідає за:
- ✅ Відправка email нотифікацій
- ✅ SMS нотифікації (опційно)
- ✅ Push notifications (опційно)
- ✅ Consuming events (OrderCreated, OrderShipped, PaymentFailed)
- ✅ Email templates (Razor/Handlebars)
- ✅ Retry logic для failed deliveries
- ✅ Notification history

---

## Архітектура

### Технології

| Компонент | Технологія | Призначення |
|-----------|------------|-------------|
| **Framework** | ASP.NET Core 9.0 | Background service |
| **Message Bus** | RabbitMQ + MassTransit | Event consumption |
| **Email** | MailKit / SendGrid | Email sending |
| **Templates** | RazorLight | Email templates |
| **Storage** | PostgreSQL (optional) | Notification history |

---

## Event Consumers

### OrderCreatedConsumer

```csharp
// EShop.Notification.Application/Consumers/OrderCreatedConsumer.cs

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        _logger.LogInformation(
            "Sending order confirmation email for order {OrderId}", 
            context.Message.OrderId);

        var email = new OrderConfirmationEmail
        {
            To = context.Message.UserEmail,
            OrderId = context.Message.OrderId,
            TotalAmount = context.Message.TotalAmount,
            Items = context.Message.Items
        };

        await _emailService.SendOrderConfirmationAsync(email);
    }
}
```

### OrderShippedConsumer

```csharp
public class OrderShippedConsumer : IConsumer<OrderShippedEvent>
{
    private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<OrderShippedEvent> context)
    {
        var email = new OrderShippedEmail
        {
            To = context.Message.UserEmail,
            OrderId = context.Message.OrderId,
            TrackingNumber = context.Message.TrackingNumber
        };

        await _emailService.SendOrderShippedAsync(email);
    }
}
```

---

## Email Service

```csharp
// EShop.Notification.Infrastructure/Services/EmailService.cs

public class EmailService : IEmailService
{
    private readonly ITemplateRenderer _templateRenderer;
    private readonly SmtpSettings _smtpSettings;
    private readonly ILogger<EmailService> _logger;

    public async Task SendOrderConfirmationAsync(OrderConfirmationEmail email)
    {
        var html = await _templateRenderer.RenderAsync(
            "OrderConfirmation", 
            email);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _smtpSettings.SenderName, 
            _smtpSettings.SenderEmail));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = $"Order Confirmation - #{email.OrderId}";
        message.Body = new TextPart(TextFormat.Html) { Text = html };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            _smtpSettings.Host, 
            _smtpSettings.Port, 
            SecureSocketOptions.StartTls);

        await client.AuthenticateAsync(
            _smtpSettings.Username, 
            _smtpSettings.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Order confirmation email sent to {Email}", email.To);
    }
}
```

---

## Email Templates

### OrderConfirmation.cshtml

```html
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; }
        .header { background: #007bff; color: white; padding: 20px; }
        .content { padding: 20px; }
        .item { border-bottom: 1px solid #ccc; padding: 10px 0; }
        .total { font-size: 20px; font-weight: bold; }
    </style>
</head>
<body>
    <div class="header">
        <h1>Order Confirmation</h1>
    </div>
    <div class="content">
        <p>Thank you for your order!</p>
        <p>Order ID: <strong>@Model.OrderId</strong></p>
        
        <h2>Items:</h2>
        @foreach (var item in Model.Items)
        {
            <div class="item">
                <strong>@item.ProductName</strong> x @item.Quantity
                <span style="float: right;">$@item.SubTotal</span>
            </div>
        }
        
        <p class="total">Total: $@Model.TotalAmount</p>
        
        <p>We'll send you another email when your order ships.</p>
    </div>
</body>
</html>
```

---

## Configuration

### appsettings.json

```json
{
  "Smtp": {
    "Host": "smtp.mailtrap.io",
    "Port": 587,
    "Username": "your-username",
    "Password": "your-password",
    "SenderEmail": "noreply@eshop.com",
    "SenderName": "E-Shop"
  },
  
  "RabbitMQ": {
    "Host": "localhost"
  },
  
  "Templates": {
    "Path": "Templates/Email"
  }
}
```

---

## SendGrid Integration (Production)

```csharp
public class SendGridEmailService : IEmailService
{
    private readonly SendGridClient _client;

    public async Task SendOrderConfirmationAsync(OrderConfirmationEmail email)
    {
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress("noreply@eshop.com", "E-Shop"),
            to: new EmailAddress(email.To),
            subject: $"Order Confirmation - #{email.OrderId}",
            plainTextContent: "Your order has been confirmed.",
            htmlContent: await _templateRenderer.RenderAsync("OrderConfirmation", email)
        );

        var response = await _client.SendEmailAsync(msg);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to send email via SendGrid: {Status}", 
                response.StatusCode);
        }
    }
}
```

---

## Retry Policy

```csharp
// MassTransit retry configuration

x.AddConsumer<OrderCreatedConsumer>(cfg =>
{
    cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
});
```

---

## Notification Types

- ✅ **Order Confirmation** - Після створення замовлення
- ✅ **Payment Success** - Після успішної оплати
- ✅ **Payment Failed** - Якщо оплата не пройшла
- ✅ **Order Shipped** - Коли замовлення відправлено
- ✅ **Order Delivered** - Коли замовлення доставлено
- ✅ **Password Reset** - При запиті на скидання пароля
- ✅ **Welcome Email** - При реєстрації

---

## Наступні кроки

- ✅ [API Gateway](api-gateway.md)
- ✅ [Observability](../../06-infrastructure/observability.md)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
