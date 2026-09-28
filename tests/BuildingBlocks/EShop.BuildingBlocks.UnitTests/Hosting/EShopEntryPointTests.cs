using System.Reflection;
using EShop.BuildingBlocks.Infrastructure.Hosting;

namespace EShop.BuildingBlocks.UnitTests.Hosting;

/// <summary>
/// docker-ci DC-37. Every <c>Program.cs</c> returns 1 from a failed start only when it is the process's entry point,
/// and rethrows otherwise. This test host is the case that must rethrow: it runs other assemblies' code from its own
/// entry assembly, exactly as <c>WebApplicationFactory</c> runs a service's <c>Main</c>. If the answer here flipped,
/// every <c>StartupGuardTests</c> would lose the guard's message (and go red with "The entry point exited without
/// ever building an IHost").
/// </summary>
[TestFixture]
public class EShopEntryPointTests
{
    [Test]
    public void AnAssemblyRunByAHost_IsNotTheProcessEntryPoint()
    {
        Assert.That(EShopEntryPoint.IsProcessEntryPoint(typeof(EShopEntryPointTests).Assembly), Is.False);
    }

    [Test]
    public void UnderAHost_ExitCodeFor_RethrowsTheSameException_WithItsOriginalStackTrace()
    {
        var original = Thrown();

        var rethrown = Assert.Throws<InvalidOperationException>(
            () => EShopEntryPoint.ExitCodeFor(original, typeof(EShopEntryPointTests).Assembly));

        Assert.Multiple(() =>
        {
            Assert.That(rethrown, Is.SameAs(original));
            Assert.That(rethrown!.StackTrace, Does.Contain(nameof(Thrown)), "the frame where it was first thrown");
        });
    }

    [Test]
    public void AsTheProcess_ExitCodeFor_Returns1()
    {
        var entry = Assembly.GetEntryAssembly();
        Assume.That(entry, Is.Not.Null, "the test runner reports no entry assembly");

        Assert.That(EShopEntryPoint.ExitCodeFor(new InvalidOperationException("refused"), entry!), Is.EqualTo(1));
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("JWT SecretKey contains placeholder pattern 'CHANGE_ME'");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Test]
    public void TheAssemblyTheProcessStartedWith_IsTheEntryPoint()
    {
        var entry = Assembly.GetEntryAssembly();
        Assume.That(entry, Is.Not.Null, "the test runner reports no entry assembly");

        Assert.That(EShopEntryPoint.IsProcessEntryPoint(entry!), Is.True);
    }
}
