using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptDebugModelsTests
{
    [Fact]
    public void ScriptLocation_formats_source_line_and_column()
    {
        var location = new ScriptLocation("script.js", 2, 3);

        Assert.Equal("script.js:2:3", location.ToString());
    }

    [Fact]
    public void ScriptVariable_displays_null_values_as_null()
    {
        var variable = new ScriptVariable("name", null, "object");

        Assert.Equal("null", variable.DisplayValue);
    }

    [Fact]
    public void ScriptPauseInfo_defaults_collections_to_empty()
    {
        var pauseInfo = new ScriptPauseInfo
        {
            Location = new ScriptLocation("script.js", 1, 1),
            Reason = PauseReason.Breakpoint
        };

        Assert.Empty(pauseInfo.SourceLines);
        Assert.Empty(pauseInfo.Variables);
        Assert.Empty(pauseInfo.CallStack);
    }
}
