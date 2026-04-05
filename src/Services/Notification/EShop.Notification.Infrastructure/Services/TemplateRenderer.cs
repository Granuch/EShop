using System.Collections.Concurrent;
using EShop.Notification.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace EShop.Notification.Infrastructure.Services;

public sealed class TemplateRenderer : ITemplateRenderer
{
    private readonly string _templatesRoot;
    private readonly ConcurrentDictionary<string, string> _templateCache = new(StringComparer.OrdinalIgnoreCase);

    public TemplateRenderer(IHostEnvironment environment)
    {
        _templatesRoot = Path.Combine(environment.ContentRootPath, "src", "Services", "Notification", "EShop.Notification.Infrastructure", "Templates");

        if (!Directory.Exists(_templatesRoot))
        {
            _templatesRoot = Path.Combine(AppContext.BaseDirectory, "Templates");
        }
    }

    public async Task<string> RenderAsync(string templateName, IReadOnlyDictionary<string, string> tokens, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("Template name is required.", nameof(templateName));
        }

        var template = await GetTemplateAsync(templateName, ct);
        var rendered = template;

        foreach (var token in tokens)
        {
            rendered = rendered.Replace($"{{{{{token.Key}}}}}", token.Value ?? string.Empty, StringComparison.Ordinal);
        }

        return rendered;
    }

    private async Task<string> GetTemplateAsync(string templateName, CancellationToken ct)
    {
        if (_templateCache.TryGetValue(templateName, out var cachedTemplate))
        {
            return cachedTemplate;
        }

        var path = Path.Combine(_templatesRoot, $"{templateName}.html");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Template '{templateName}' not found.", path);
        }

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        var template = await reader.ReadToEndAsync(ct);

        _templateCache[templateName] = template;
        return template;
    }
}
