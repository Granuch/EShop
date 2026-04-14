using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using EShop.Notification.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;

namespace EShop.Notification.Infrastructure.Services;

public sealed class TemplateRenderer : ITemplateRenderer, IDisposable
{
    private readonly string _templatesRoot;
    private readonly MemoryCache _templateCache = new(new MemoryCacheOptions
    {
        SizeLimit = 64
    });

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
            var encodedValue = HtmlEncoder.Default.Encode(token.Value ?? string.Empty);
            rendered = rendered.Replace($"{{{{{token.Key}}}}}", encodedValue, StringComparison.Ordinal);
        }

        return rendered;
    }

    private async Task<string> GetTemplateAsync(string templateName, CancellationToken ct)
    {
        if (_templateCache.TryGetValue(templateName, out string? cachedTemplate)
            && cachedTemplate is not null)
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

        _templateCache.Set(
            templateName,
            template,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                Size = 1
            });

        return template;
    }

    public void Dispose() => _templateCache.Dispose();
}
