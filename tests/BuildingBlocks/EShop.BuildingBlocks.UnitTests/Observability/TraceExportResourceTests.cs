using System.Diagnostics;
using System.Net;
using System.Text;
using EShop.BuildingBlocks.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace EShop.BuildingBlocks.UnitTests.Observability;

/// <summary>
/// docker-ci DC-35: from 2026-03-08 until docker-ci Stage 6b every span left every service with an EMPTY resource,
/// so Jaeger filed all of them under <c>OTLPResourceNoServiceName</c> and the collector's span metrics stayed empty.
/// The OTLP exporter had been built by hand and wrapped in another exporter; the SDK hands its provider (the source
/// of the resource) only to the exporter it is given, so the inner one never saw it. Nothing failed or logged.
///
/// This drives the real <see cref="OpenTelemetryServiceCollectionExtensions.AddEShopOpenTelemetry"/> registration
/// against an in-process OTLP/gRPC receiver and reads the bytes that reach it. Protobuf writes strings as raw UTF-8,
/// so the resource attributes are visible in the request body without decoding it.
/// </summary>
[TestFixture]
public class TraceExportResourceTests
{
    private const string ServiceName = "EShop.TraceProbe.API";
    private const string SourceName = "EShop.TraceProbe";
    private const string ExportPath = "/opentelemetry.proto.collector.trace.v1.TraceService/Export";

    [Test]
    public async Task ExportedSpans_CarryTheServiceResource()
    {
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var collector = await StartCollectorAsync(received);
        var endpoint = collector.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "true",
                ["OpenTelemetry:OtlpEndpoint"] = endpoint,
                ["OpenTelemetry:SamplingRatio"] = "1.0",
                ["OpenTelemetry:MetricsEnabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEShopOpenTelemetry(configuration, ServiceName, "9.9.9", new SandboxEnvironment(), SourceName);
        await using var provider = services.BuildServiceProvider();
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using (var source = new ActivitySource(SourceName))
        using (source.StartActivity("probe"))
        {
        }

        tracerProvider.ForceFlush(10_000);
        var body = Encoding.UTF8.GetString(await received.Task.WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("probe"), "the span itself was not exported");
            Assert.That(body, Does.Contain("service.name"));
            Assert.That(body, Does.Contain(ServiceName));
            Assert.That(body, Does.Contain("deployment.environment"));
            Assert.That(body, Does.Contain("Sandbox"));
        });
    }

    private static async Task<WebApplication> StartCollectorAsync(TaskCompletionSource<byte[]> received)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // gRPC over plain http:// is HTTP/2 with prior knowledge, which Kestrel serves only on an HTTP/2-only endpoint.
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));

        var app = builder.Build();
        app.MapPost(ExportPath, async (HttpContext context) =>
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            received.TrySetResult(buffer.ToArray());

            // An empty ExportTraceServiceResponse: one uncompressed gRPC frame of length zero, then status OK.
            context.Response.ContentType = "application/grpc";
            context.Response.AppendTrailer("grpc-status", "0");
            await context.Response.Body.WriteAsync(new byte[5]);
        });

        await app.StartAsync();
        return app;
    }

    private sealed class SandboxEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Sandbox";
        public string ApplicationName { get; set; } = ServiceName;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
