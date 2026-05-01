using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// Describes one script execution request.
/// </summary>
public sealed record ScriptExecutionRequest
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyVariables =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    /// <summary>
    /// The script source text.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// The language the host should use to execute the source.
    /// </summary>
    public ScriptLanguage Language { get; init; } = ScriptLanguage.Unknown;

    /// <summary>
    /// Optional display name, file name, or logical module path for diagnostics.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional timeout requested for this execution.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Variables the host may expose to the script before execution.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; init; } = EmptyVariables;
}
