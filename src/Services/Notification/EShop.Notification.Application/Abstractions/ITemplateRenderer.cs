namespace EShop.Notification.Application.Abstractions;

public interface ITemplateRenderer
{
    Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, string> tokens, CancellationToken ct = default);
}
