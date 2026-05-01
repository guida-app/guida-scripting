using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptingRuntimeInfoTests
{
    [Fact]
    public void PackageId_matches_runtime_package_name()
    {
        Assert.Equal("Guida.Scripting", ScriptingRuntimeInfo.PackageId);
    }
}
