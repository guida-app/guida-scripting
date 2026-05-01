namespace Guida.Scripting;

/// <summary>
/// Detects scripting languages from file names and paths.
/// </summary>
public static class ScriptLanguageDetector
{
    /// <summary>
    /// Detects the script language from a file name or path extension.
    /// </summary>
    public static ScriptLanguage Detect(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return ScriptLanguage.Unknown;
        }

        return Path.GetExtension(fileNameOrPath.Trim()).ToLowerInvariant() switch
        {
            ".js" or ".mjs" or ".cjs" => ScriptLanguage.JavaScript,
            ".ts" => ScriptLanguage.TypeScript,
            ".lua" => ScriptLanguage.Lua,
            ".janet" => ScriptLanguage.Janet,
            _ => ScriptLanguage.Unknown
        };
    }
}
