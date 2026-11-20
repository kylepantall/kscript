using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// Guards for the Native AOT build. Packages are loaded by type name, which the AOT compiler can't follow, so each
/// built-in package needs a DynamicDependency on <see cref="PackageRegistry.Get"/>; without one, a native kscript
/// says the package "isn't installed".
/// </summary>
public class NativeAotTests
{
    [Fact]
    public void Every_built_in_package_is_kept_for_native_aot()
    {
        var kept = typeof(PackageRegistry).GetMethod(nameof(PackageRegistry.Get))!
            .GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(a => a.TypeName + ", " + a.AssemblyName)
            .ToHashSet();

        var builtIn = PackageRegistry.BuiltIn.Skip(1).Select(p => p.TypeName).ToHashSet();

        Assert.Equal(builtIn.OrderBy(t => t), kept.OrderBy(t => t));
        Assert.All(PackageRegistry.BuiltIn.Skip(1), p => Assert.NotNull(PackageRegistry.Default.Get(p.Name)));
    }
}
