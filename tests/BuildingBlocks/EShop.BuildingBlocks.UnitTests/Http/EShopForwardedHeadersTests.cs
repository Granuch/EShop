using System.Net;
using EShop.BuildingBlocks.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.BuildingBlocks.UnitTests.Http;

/// <summary>
/// H3. Covers <see cref="EShopForwardedHeaders"/>, which the root guide listed as untested and
/// which every IP-derived control in the platform depends on — rate-limit partitions, brute-force
/// tracking, <c>LastLoginIp</c>.
///
/// <para>
/// Both halves of this class fail silently. <c>AddEShopForwardedHeaders</c> returning <c>false</c>
/// means the middleware is never registered, so <c>X-Forwarded-For</c> is ignored and every request
/// appears to come from the gateway — nothing throws, nothing 500s, the platform simply shares one
/// rate-limit bucket between all users. And a partition key that does not normalise IPv4-mapped
/// IPv6 hands a dual-stack caller two allowances, which looks like nothing at all.
/// </para>
/// </summary>
[TestFixture]
public class EShopForwardedHeadersTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static HttpContext ContextFrom(string? remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        return context;
    }

    /// <summary>
    /// The failure that shipped: with neither setting present the helper returns false and
    /// <c>UseEShopForwardedHeaders</c> registers nothing. That is deliberate — applying the
    /// middleware with no known proxy or network is <i>unsafe</i>, since it would honour a
    /// client-supplied <c>X-Forwarded-For</c> — but it is also why an unconfigured deployment
    /// degrades in silence.
    /// </summary>
    [Test]
    public void ReturnsFalseWhenNeitherProxiesNorNetworksAreConfigured()
    {
        var enabled = new ServiceCollection().AddEShopForwardedHeaders(Configuration());

        Assert.That(enabled, Is.False);
    }

    /// <summary>
    /// An empty array is what the tracked <c>appsettings.json</c> files ship
    /// (<c>"KnownProxies": []</c>), so this is the real-world "configured but not really" case.
    /// </summary>
    [Test]
    public void ReturnsFalseForAnEmptyKnownProxiesArray()
    {
        var enabled = new ServiceCollection()
            .AddEShopForwardedHeaders(Configuration(("ForwardedHeaders:KnownProxies:0", "")));

        Assert.That(enabled, Is.False);
    }

    /// <summary>
    /// The private ranges <c>docker-compose.yml</c> and <c>k8s/02-configmap.yaml</c> trust: the three RFC 1918
    /// ranges plus IPv6 unique-local. The fourth is not optional. Docker can give <c>eshop-network</c> an IPv6
    /// subnet, and the gateway then reaches every service over IPv6 first. Trusting only the IPv4 ranges made each
    /// service ignore <c>X-Forwarded-For</c> and put every client in one rate-limit bucket.
    /// </summary>
    private static readonly (string Key, string Value)[] DeployedNetworks =
    [
        ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"),
        ("ForwardedHeaders:KnownNetworks:1", "172.16.0.0/12"),
        ("ForwardedHeaders:KnownNetworks:2", "192.168.0.0/16"),
        ("ForwardedHeaders:KnownNetworks:3", "fc00::/7")
    ];

    /// <summary>
    /// If this goes red, the Sandbox stack is back to one shared rate-limit bucket for the entire user base.
    /// </summary>
    [Test]
    public void EnablesForThePrivateRangesTheDeploymentsConfigure()
    {
        var services = new ServiceCollection();

        var enabled = services.AddEShopForwardedHeaders(Configuration(DeployedNetworks));

        Assert.That(enabled, Is.True);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.That(options.KnownIPNetworks, Has.Count.EqualTo(4));
        Assert.That(options.ForwardLimit, Is.EqualTo(1));
    }

    /// <summary>
    /// The address the gateway actually connects from in the compose stack (an <c>fdc7:…</c> address on the
    /// network's IPv6 subnet), driven through the real middleware. Without <c>fc00::/7</c> the client address
    /// stays the gateway's.
    /// </summary>
    [TestCase("fdc7:cf73:f805:1::12")]
    [TestCase("172.18.0.18")]
    public async Task TakesTheClientAddressFromAPrivateNetworkProxy(string gatewayAddress)
    {
        var seen = await ClientAddressSeenBehind(gatewayAddress, "198.51.100.7");

        Assert.That(seen, Is.EqualTo(IPAddress.Parse("198.51.100.7")));
    }

    /// <summary>The control: a public peer is not a proxy, so its X-Forwarded-For is ignored.</summary>
    [TestCase("2001:db8::1")]
    [TestCase("203.0.113.9")]
    public async Task IgnoresXForwardedForFromAPublicPeer(string peer)
    {
        var seen = await ClientAddressSeenBehind(peer, "198.51.100.7");

        Assert.That(seen, Is.EqualTo(IPAddress.Parse(peer)));
    }

    /// <summary>
    /// What the repository ships. No test host reads these files, so only reading them can notice one service
    /// losing the IPv6 entry. Every app service in compose, and the shared k8s ConfigMap, must trust exactly
    /// <see cref="DeployedNetworks"/>.
    /// </summary>
    [TestCase("identity-api")]
    [TestCase("api-gateway")]
    [TestCase("basket-api")]
    [TestCase("catalog-api")]
    [TestCase("ordering-api")]
    [TestCase("payment-api")]
    [TestCase("notification-api")]
    public void Compose_TrustsThePrivateRanges(string service)
    {
        Assert.That(
            KnownNetworkLines(ComposeServiceBlock(service)),
            Is.EqualTo(DeployedNetworks.Select(ExpectedLine).ToArray()));
    }

    [Test]
    public void Kubernetes_TrustsThePrivateRanges()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "k8s", "02-configmap.yaml"))
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'));

        Assert.That(KnownNetworkLines(lines), Is.EqualTo(DeployedNetworks.Select(ExpectedLine).ToArray()));
    }

    private static string ExpectedLine((string Key, string Value) setting)
        => $"{setting.Key.Replace(":", "__", StringComparison.Ordinal)}: \"{setting.Value}\"";

    private static string[] KnownNetworkLines(IEnumerable<string> lines)
        => lines.Where(line => line.StartsWith("ForwardedHeaders__KnownNetworks__", StringComparison.Ordinal)).ToArray();

    private static async Task<IPAddress?> ClientAddressSeenBehind(string peer, string forwardedFor)
    {
        var services = new ServiceCollection().AddLogging();
        var enabled = services.AddEShopForwardedHeaders(Configuration(DeployedNetworks));
        await using var provider = services.BuildServiceProvider();

        IPAddress? seen = null;
        var app = new ApplicationBuilder(provider);
        app.UseEShopForwardedHeaders(enabled);
        app.Run(context =>
        {
            seen = context.Connection.RemoteIpAddress;
            return Task.CompletedTask;
        });

        var request = ContextFrom(peer);
        request.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await app.Build()(request);

        return seen;
    }

    /// <summary>The uncommented, trimmed lines of one service in docker-compose.yml, up to the next service.</summary>
    private static List<string> ComposeServiceBlock(string service)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docker-compose.yml"));
        var start = Array.IndexOf(lines, $"  {service}:");
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"docker-compose.yml has no {service} service");

        var nextService = new System.Text.RegularExpressions.Regex(@"^(  )?[A-Za-z0-9_-]+:\s*$");
        return lines
            .Skip(start + 1)
            .TakeWhile(line => !nextService.IsMatch(line))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EShop.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("EShop.slnx not found above the test directory.");
    }

    /// <summary>
    /// A CIDR that cannot be parsed is logged and dropped, not thrown — so a config of nothing but
    /// bad entries reports "not configured" rather than failing loudly. This is how an
    /// unsubstituted <c>"#{TRUSTED_PROXY_IP}#"</c> token silently disables forwarded headers.
    /// </summary>
    [Test]
    public void DropsUnparseableEntriesAndReportsNotConfigured()
    {
        var enabled = new ServiceCollection().AddEShopForwardedHeaders(Configuration(
            ("ForwardedHeaders:KnownProxies:0", "#{TRUSTED_PROXY_IP}#"),
            ("ForwardedHeaders:KnownNetworks:0", "not-a-cidr")));

        Assert.That(enabled, Is.False);
    }

    /// <summary>
    /// The normalisation the Catalog and Basket global limiters were missing: without it a
    /// dual-stack client gets one bucket as <c>::ffff:1.2.3.4</c> and another as <c>1.2.3.4</c>,
    /// i.e. double its allowance, with nothing to show for it in any log.
    /// </summary>
    [Test]
    public void MapsIpv4MappedIpv6OntoTheSamePartitionAsPlainIpv4()
    {
        var mapped = EShopForwardedHeaders.GetClientPartitionKey(ContextFrom("::ffff:1.2.3.4"));
        var plain = EShopForwardedHeaders.GetClientPartitionKey(ContextFrom("1.2.3.4"));

        Assert.That(mapped, Is.EqualTo(plain));
        Assert.That(mapped, Is.EqualTo("1.2.3.4"));
    }

    [Test]
    public void KeepsGenuineIpv6AddressesDistinct()
    {
        Assert.That(
            EShopForwardedHeaders.GetClientPartitionKey(ContextFrom("2001:db8::1")),
            Is.Not.EqualTo(EShopForwardedHeaders.GetClientPartitionKey(ContextFrom("2001:db8::2"))));
    }

    /// <summary>
    /// TestServer has no socket, so this is the common case under WebApplicationFactory — every
    /// test request shares the "anonymous" bucket, which is why rate-limit fixtures have to fake a
    /// peer address to test anything at all.
    /// </summary>
    [Test]
    public void FallsBackToAnonymousWhenThereIsNoPeerAddress()
    {
        Assert.That(
            EShopForwardedHeaders.GetClientPartitionKey(ContextFrom(null)),
            Is.EqualTo("anonymous"));
    }
}
