using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptLanguageDetectorTests
{
    [Theory]
    [InlineData("script.js", ScriptLanguage.JavaScript)]
    [InlineData("script.mjs", ScriptLanguage.JavaScript)]
    [InlineData("script.cjs", ScriptLanguage.JavaScript)]
    [InlineData("script.ts", ScriptLanguage.TypeScript)]
    [InlineData("script.lua", ScriptLanguage.Lua)]
    [InlineData("script.janet", ScriptLanguage.Janet)]
    [InlineData("SCRIPT.JS", ScriptLanguage.JavaScript)]
    [InlineData(@"C:\workspace\scripts\job.LUA", ScriptLanguage.Lua)]
    [InlineData("/workspace/scripts/job.janet", ScriptLanguage.Janet)]
    public void Detect_returns_language_for_known_extensions(string fileNameOrPath, ScriptLanguage expected)
    {
        Assert.Equal(expected, ScriptLanguageDetector.Detect(fileNameOrPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("script")]
    [InlineData("script.txt")]
    [InlineData("script.jsx")]
    public void Detect_returns_unknown_for_missing_or_unknown_extensions(string? fileNameOrPath)
    {
        Assert.Equal(ScriptLanguage.Unknown, ScriptLanguageDetector.Detect(fileNameOrPath));
    }
}
