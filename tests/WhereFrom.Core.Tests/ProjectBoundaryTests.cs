using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace WhereFrom.Core.Tests;

public class ProjectBoundaryTests
{
    [Fact]
    public void CoreTargetsNet10WithoutAPlatformRequirement()
    {
        var assembly = Assembly.Load("WhereFrom.Core");

        Assert.Equal(".NETCoreApp,Version=v10.0",
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.Null(assembly.GetCustomAttribute<TargetPlatformAttribute>());
        Assert.Empty(assembly.GetCustomAttributes<SupportedOSPlatformAttribute>());
    }
}
