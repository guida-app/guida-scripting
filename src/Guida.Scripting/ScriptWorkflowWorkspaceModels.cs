using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// Standard workflow workspace paths and file names.
/// </summary>
public static class ScriptWorkflowWorkspaceLayout
{
    public const string ScriptsDirectory = "scripts";
    public const string LibrariesDirectory = "lib";
    public const string ViewsDirectory = "views";
    public const string WorkflowsDirectory = "workflows";

    public const string EventsConfigFile = "events.json";
    public const string QueuesConfigFile = "queues.json";
    public const string ToolsConfigFile = "tools.json";
    public const string WorkflowManifestFile = "workflow.json";
    public const string WorkflowLedgerSchemaFile = "workflow-ledger.schema.json";

    public static readonly IReadOnlyList<string> DefaultScriptExtensions =
        new ReadOnlyCollection<string>([".js", ".lua", ".janet"]);

    public static string WorkflowPath(string workflowName) =>
        Combine(WorkflowsDirectory, workflowName);

    public static string WorkflowFilePath(string workflowName, string fileName) =>
        Combine(WorkflowsDirectory, workflowName, fileName);

    public static string WorkflowScriptsPath(string workflowName) =>
        Combine(WorkflowsDirectory, workflowName, ScriptsDirectory);

    public static string WorkflowLibrariesPath(string workflowName) =>
        Combine(WorkflowsDirectory, workflowName, LibrariesDirectory);

    public static string WorkflowViewsPath(string workflowName) =>
        Combine(WorkflowsDirectory, workflowName, ViewsDirectory);

    internal static string Combine(params string?[] parts) =>
        string.Join(
            "/",
            parts
                .Select(part => part?.Trim().Trim('/', '\\'))
                .Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>
/// Options used while discovering workflow workspace layout information.
/// </summary>
public sealed record ScriptWorkflowWorkspaceDiscoveryOptions
{
    public IReadOnlyList<string> ScriptExtensions { get; init; } =
        ScriptWorkflowWorkspaceLayout.DefaultScriptExtensions;
}

/// <summary>
/// Stable error codes for workflow workspace discovery failures.
/// </summary>
public enum ScriptWorkflowWorkspaceErrorCode
{
    InvalidRequest,
    WorkspaceError
}

/// <summary>
/// Describes an expected workflow workspace discovery failure.
/// </summary>
public sealed record ScriptWorkflowWorkspaceError
{
    public ScriptWorkflowWorkspaceError(
        ScriptWorkflowWorkspaceErrorCode code,
        string path,
        string message,
        ScriptWorkspaceError? workspaceError = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Path = path;
        Message = message;
        WorkspaceError = workspaceError;
    }

    /// <summary>Stable workflow workspace error code.</summary>
    public ScriptWorkflowWorkspaceErrorCode Code { get; }

    /// <summary>Logical workspace path related to the error, or empty when not applicable.</summary>
    public string Path { get; }

    /// <summary>Host-readable error message.</summary>
    public string Message { get; }

    /// <summary>Underlying workspace error when one caused the failure.</summary>
    public ScriptWorkspaceError? WorkspaceError { get; }

    /// <summary>
    /// Converts the workflow workspace error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}

/// <summary>
/// Result of a workflow workspace discovery operation.
/// </summary>
public sealed record ScriptWorkflowWorkspaceResult<T>
{
    private ScriptWorkflowWorkspaceResult(bool success, T? value, ScriptWorkflowWorkspaceError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the workflow workspace operation succeeded.</summary>
    public bool Success { get; }

    /// <summary>Returned value when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Error information when the operation failed.</summary>
    public ScriptWorkflowWorkspaceError? Error { get; }

    /// <summary>Creates a successful workflow workspace result.</summary>
    public static ScriptWorkflowWorkspaceResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>Creates a failed workflow workspace result.</summary>
    public static ScriptWorkflowWorkspaceResult<T> Failed(ScriptWorkflowWorkspaceError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScriptWorkflowWorkspaceResult<T>(false, default, error);
    }
}

/// <summary>
/// Scope of a discovered workflow workspace file.
/// </summary>
public enum ScriptWorkflowWorkspaceScope
{
    Global,
    Workflow
}

/// <summary>
/// Kind of discovered workflow workspace config file.
/// </summary>
public enum ScriptWorkflowWorkspaceConfigKind
{
    Events,
    Queues,
    Tools,
    Manifest,
    LedgerSchema
}

/// <summary>
/// Discovered workspace file used by workflow features.
/// </summary>
public sealed record ScriptWorkflowWorkspaceFile
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ScriptWorkflowWorkspaceScope Scope { get; init; }
    public string? WorkflowName { get; init; }
    public long? Length { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
}

/// <summary>
/// Discovered workspace config file used by workflow features.
/// </summary>
public sealed record ScriptWorkflowWorkspaceConfigFile
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ScriptWorkflowWorkspaceConfigKind Kind { get; init; }
    public ScriptWorkflowWorkspaceScope Scope { get; init; }
    public string? WorkflowName { get; init; }
    public long? Length { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
}

/// <summary>
/// Discovered workflow folder summary.
/// </summary>
public sealed record ScriptWorkflowDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public bool HasEvents { get; init; }
    public bool HasWorkers { get; init; }
    public bool HasTools { get; init; }
    public bool HasViews { get; init; }
    public bool HasLedgerSchema { get; init; }
    public int ScriptCount { get; init; }
    public string? ManifestError { get; init; }
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> Scripts { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> Libraries { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> Views { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceConfigFile> ConfigFiles { get; init; } = Array.Empty<ScriptWorkflowWorkspaceConfigFile>();
}

/// <summary>
/// Snapshot of the workflow-relevant workspace layout.
/// </summary>
public sealed record ScriptWorkflowWorkspaceSnapshot
{
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalScripts { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalLibraries { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalViews { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceConfigFile> GlobalConfigFiles { get; init; } = Array.Empty<ScriptWorkflowWorkspaceConfigFile>();
    public IReadOnlyList<ScriptWorkflowDefinition> Workflows { get; init; } = Array.Empty<ScriptWorkflowDefinition>();
}

/// <summary>
/// Workflow ledger schema JSON loaded from workflow folders.
/// </summary>
public sealed record ScriptWorkflowWorkspaceLedgerSchemas
{
    public IReadOnlyDictionary<string, string> SchemaJsonByWorkflow { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public ScriptWorkflowLedgerSchemaValidator Validator { get; init; } =
        ScriptWorkflowLedgerSchemaValidator.Empty;
}
