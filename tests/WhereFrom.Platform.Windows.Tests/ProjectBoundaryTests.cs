using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace WhereFrom.Platform.Windows.Tests;

public class ProjectBoundaryTests
{
    [Fact]
    public void WindowsAssemblyTargetsWindowsAndLoadsOnWindows()
    {
        Assert.True(OperatingSystem.IsWindows(), "Run this test project on Windows.");

        var assembly = Assembly.Load("WhereFrom.Platform.Windows");
        var platform = assembly.GetCustomAttribute<TargetPlatformAttribute>();

        Assert.Equal(".NETCoreApp,Version=v10.0",
            assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.NotNull(platform);
        Assert.StartsWith("Windows", platform.PlatformName, StringComparison.OrdinalIgnoreCase);
    }
}
