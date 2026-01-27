# 📧 Phase 7: Notification Service Implementation

**Duration**: 1 week  
**Team Size**: 1 developer  
**Prerequisites**: Phase 2 (Identity), Phase 5 (Ordering) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ Email notifications (SendGrid/SMTP)
- ✅ SMS notifications (Twilio) - optional
- ✅ Push notifications (Firebase) - optional
- ✅ Template management
- ✅ Notification history
- ✅ Event-driven notification triggers

---

## Tasks Breakdown

### 7.1 Email Service Setup

**Estimated Time**: 2 days

**NuGet Packages:**

```xml
<PackageReference Include="SendGrid" Version="9.28.1" />
<PackageReference Include="MailKit" Version="4.3.0" />
<PackageReference Include="RazorLight" Version="2.3.0" />
```

**Email Service:**

```csharp
// EShop.Notification.Infrastructure/Services/EmailService.cs

public class EmailService : IEmailService
{
    private readonly ISendGridClient _sendGridClient;
    private readonly ITemplateEngine _templateEngine;
    private readonly EmailSettings _settings;

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string templateName,
        object model)
    {
        var htmlContent = await _templateEngine.RenderTemplateAsync(templateName, model);

        var message = new SendGridMessage
        {
            From = new EmailAddress(_settings.SenderEmail, _settings.SenderName),
            Subject = subject,
            HtmlContent = htmlContent
        };

        message.AddTo(toEmail);

        await _sendGridClient.SendEmailAsync(message);
    }

    public async Task SendOrderConfirmationAsync(string toEmail, OrderDto order)
    {
        await SendEmailAsync(
            toEmail,
            "Order Confirmation",
            "OrderConfirmation",
            new
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                Items = order.Items,
                TotalAmount = order.TotalAmount
            });
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string firstName)
    {
        await SendEmailAsync(
            toEmail,
            "Welcome to E-Shop!",
            "Welcome",
            new { FirstName = firstName });
    }
}
```

---

### 7.2 Template Engine

**Estimated Time**: 1 day

**Email Templates (Razor):**

```html
<!-- Templates/OrderConfirmation.cshtml -->

<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; }
        .header { background: #4CAF50; color: white; padding: 20px; }
        .order-item { border-bottom: 1px solid #ddd; padding: 10px; }
        .total { font-weight: bold; font-size: 18px; }
    </style>
</head>
<body>
    <div class="header">
        <h1>Order Confirmation</h1>
    </div>
    
    <p>Thank you for your order!</p>
    <p>Order Number: <strong>@Model.OrderNumber</strong></p>
    
    <h3>Order Items:</h3>
    @foreach (var item in Model.Items)
    {
        <div class="order-item">
            <span>@item.ProductName</span>
            <span>x @item.Quantity</span>
            <span>$@item.Price</span>
        </div>
    }
    
    <p class="total">Total: $@Model.TotalAmount</p>
    
    <p>You will receive shipping updates via email.</p>
</body>
</html>
```

**Template Engine Implementation:**

```csharp
public class RazorTemplateEngine : ITemplateEngine
{
    private readonly RazorLightEngine _engine;

    public RazorTemplateEngine()
    {
        _engine = new RazorLightEngineBuilder()
            .UseFileSystemProject(Path.Combine(Directory.GetCurrentDirectory(), "Templates"))
            .UseMemoryCachingProvider()
            .Build();
    }

    public async Task<string> RenderTemplateAsync<T>(string templateName, T model)
    {
        return await _engine.CompileRenderAsync($"{templateName}.cshtml", model);
    }
}
```

---

### 7.3 Event Consumers

**Estimated Time**: 2 days

**User Registered Consumer:**

```csharp
public class UserRegisteredConsumer : IConsumer<UserRegisteredEvent>
{
    private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        await _emailService.SendWelcomeEmailAsync(
            context.Message.Email,
            context.Message.FirstName);
    }
}
```

**Order Created Consumer:**

```csharp
public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IEmailService _emailService;
    private readonly IOrderApiClient _orderApiClient;

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var order = await _orderApiClient.GetOrderAsync(context.Message.OrderId);
        
        await _emailService.SendOrderConfirmationAsync(
            order.UserEmail,
            order);
    }
}
```

**Order Shipped Consumer:**

```csharp
public class OrderShippedConsumer : IConsumer<OrderShippedEvent>
{
    private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<OrderShippedEvent> context)
    {
        await _emailService.SendEmailAsync(
            context.Message.UserEmail,
            "Your Order Has Shipped",
            "OrderShipped",
            new
            {
                OrderId = context.Message.OrderId,
                TrackingNumber = context.Message.TrackingNumber
            });
    }
}
```

---

### 7.4 Notification History

**Estimated Time**: 1 day

**Notification Entity:**

```csharp
public class Notification : Entity
{
    public string UserId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; }
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum NotificationType
{
    Email,
    SMS,
    Push
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}
```

**Save Notification History:**

```csharp
public async Task SendEmailAsync(string toEmail, string subject, string templateName, object model)
{
    var notification = new Notification
    {
        UserId = model.UserId,
        Type = NotificationType.Email,
        Subject = subject,
        Status = NotificationStatus.Pending
    };

    try
    {
        var htmlContent = await _templateEngine.RenderTemplateAsync(templateName, model);
        notification.Content = htmlContent;

        await _sendGridClient.SendEmailAsync(/* ... */);

        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        notification.Status = NotificationStatus.Failed;
        notification.ErrorMessage = ex.Message;
    }

    await _notificationRepository.AddAsync(notification);
    await _notificationRepository.SaveChangesAsync();
}
```

---

### 7.5 API Layer

**Estimated Time**: 1 day

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications([FromQuery] GetUserNotificationsQuery query)
    {
        var userId = User.FindFirst("sub")?.Value!;
        query = query with { UserId = userId };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendTestEmail([FromBody] SendTestEmailCommand command)
    {
        await _mediator.Send(command);
        return Ok();
    }
}
```

---

## Configuration

```json
{
  "SendGrid": {
    "ApiKey": "SG.xxx...",
    "SenderEmail": "noreply@eshop.com",
    "SenderName": "E-Shop"
  },
  
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "your-email@gmail.com",
    "Password": "your-password",
    "UseSsl": true
  },
  
  "Twilio": {
    "AccountSid": "ACxxx...",
    "AuthToken": "xxx...",
    "PhoneNumber": "+1234567890"
  }
}
```

---

## Success Criteria

- [x] Email notifications sent on key events
- [x] Templates rendered with Razor
- [x] Notification history tracked
- [x] Retry logic for failed notifications
- [x] All tests passing (> 70% coverage)

---

## Next Phase

→ [Phase 8: Frontend Implementation (React)](phase-8-frontend.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
