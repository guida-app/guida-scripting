namespace Guida.Scripting;

/// <summary>
/// A script source document with stable host identity and metadata.
/// </summary>
public sealed record ScriptDocument
{
    /// <summary>
    /// Host-defined logical document identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name or logical path used for diagnostics.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Script source text.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Detected or host-provided script language.
    /// </summary>
    public ScriptLanguage Language { get; init; } = ScriptLanguage.Unknown;

    /// <summary>
    /// Optional stable URI for debugger or source-map style integrations.
    /// </summary>
    public Uri? SourceUri { get; init; }

    /// <summary>
    /// Optional host-defined version identifier.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Optional last modified timestamp.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; init; }

    /// <summary>
    /// Converts the document into an execution request.
    /// </summary>
    public ScriptExecutionRequest ToExecutionRequest(ScriptHostContext? hostContext = null) =>
        new()
        {
            Source = Source,
            Name = Name,
            Language = Language,
            HostContext = hostContext ?? ScriptHostContext.Empty
        };
}
