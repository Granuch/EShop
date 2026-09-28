using System.Reflection;

namespace EShop.BuildingBlocks.Infrastructure.Hosting;

/// <summary>
/// Decides how a service's <c>Program.cs</c> ends when the host fails to start (docker-ci DC-37).
///
/// <para>
/// <b>Why not just rethrow.</b> An exception rethrown out of <c>Main</c> is an unhandled exception, and the .NET
/// runtime in the Linux containers then kills the process by signal: a startup guard's refusal showed up as
/// <c>Exited (139)</c>, the code of a segmentation fault, so <c>docker ps</c> and Kubernetes reported a crash for
/// what was a clean, logged configuration error. Returning 1 from <c>Main</c> exits normally, with a code that means
/// "failed".
/// </para>
///
/// <para>
/// <b>Why not always return 1.</b> <c>WebApplicationFactory</c> and <c>dotnet ef</c> do not start the service as a
/// process. They call its <c>Main</c> on a thread of their own and learn why the host did not start only from the
/// exception <c>Main</c> throws. A <c>Main</c> that returned 1 instead would leave them with "The entry point exited
/// without ever building an IHost", and every <c>StartupGuardTests</c> would lose the guard's message. So a
/// <c>Program.cs</c> rethrows unless it is the process's own entry point.
/// </para>
/// </summary>
public static class EShopEntryPoint
{
    /// <summary>
    /// True when <paramref name="programAssembly"/> is the assembly the process was started with
    /// (<c>dotnet EShop.X.API.dll</c>); false under a test host or EF design-time tooling, which run its
    /// <c>Main</c> from their own entry assembly.
    /// </summary>
    public static bool IsProcessEntryPoint(Assembly programAssembly)
    {
        ArgumentNullException.ThrowIfNull(programAssembly);
        return Assembly.GetEntryAssembly() == programAssembly;
    }
}
