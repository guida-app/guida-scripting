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
    public string NameWithoutExtension { get; init; } = string.Empty;
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
    public string NameWithoutExtension { get; init; } = string.Empty;
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

    /// <summary>
    /// Creates the effective global plus active-workflow overlay for config and view consumers.
    /// </summary>
    public ScriptWorkflowWorkspaceOverlay CreateOverlay(string? workflowName)
    {
        var normalizedWorkflowName = TrimToNull(workflowName);
        var workflow = normalizedWorkflowName == null
            ? null
            : Workflows.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, normalizedWorkflowName, StringComparison.OrdinalIgnoreCase));

        var configs = workflow == null
            ? GlobalConfigFiles
            : GlobalConfigFiles.Concat(workflow.ConfigFiles).ToArray();
        var effectiveViews = new Dictionary<string, ScriptWorkflowWorkspaceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in GlobalViews)
        {
            effectiveViews[view.NameWithoutExtension] = view;
        }

        if (workflow != null)
        {
            foreach (var view in workflow.Views)
            {
                effectiveViews[view.NameWithoutExtension] = view;
            }
        }

        return new ScriptWorkflowWorkspaceOverlay
        {
            WorkflowName = workflow?.Name,
            WorkflowPath = workflow?.Path,
            GlobalConfigFiles = GlobalConfigFiles,
            WorkflowConfigFiles = workflow?.ConfigFiles ?? Array.Empty<ScriptWorkflowWorkspaceConfigFile>(),
            EffectiveConfigFiles = configs,
            GlobalViews = GlobalViews,
            WorkflowViews = workflow?.Views ?? Array.Empty<ScriptWorkflowWorkspaceFile>(),
            EffectiveViews = effectiveViews.Values
                .OrderBy(view => view.NameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(view => view.Path, StringComparer.Ordinal)
                .ToArray(),
            GlobalScripts = GlobalScripts,
            WorkflowScripts = workflow?.Scripts ?? Array.Empty<ScriptWorkflowWorkspaceFile>(),
            GlobalLibraries = GlobalLibraries,
            WorkflowLibraries = workflow?.Libraries ?? Array.Empty<ScriptWorkflowWorkspaceFile>()
        };
    }

    /// <summary>
    /// Gets a discovered workflow by name using the same case-insensitive matching used by host adapters.
    /// </summary>
    public ScriptWorkflowDefinition? GetWorkflow(string workflowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        return Workflows.FirstOrDefault(workflow =>
            string.Equals(workflow.Name, workflowName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Effective global plus active-workflow workspace overlay.
/// </summary>
public sealed record ScriptWorkflowWorkspaceOverlay
{
    public string? WorkflowName { get; init; }
    public string? WorkflowPath { get; init; }
    public IReadOnlyList<ScriptWorkflowWorkspaceConfigFile> GlobalConfigFiles { get; init; } = Array.Empty<ScriptWorkflowWorkspaceConfigFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceConfigFile> WorkflowConfigFiles { get; init; } = Array.Empty<ScriptWorkflowWorkspaceConfigFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceConfigFile> EffectiveConfigFiles { get; init; } = Array.Empty<ScriptWorkflowWorkspaceConfigFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalViews { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> WorkflowViews { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> EffectiveViews { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalScripts { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> WorkflowScripts { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> GlobalLibraries { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> WorkflowLibraries { get; init; } = Array.Empty<ScriptWorkflowWorkspaceFile>();
}

/// <summary>
/// Logical path helpers for workspace-backed module and config resolution.
/// </summary>
public static class ScriptWorkflowWorkspaceResolution
{
    /// <summary>
    /// Resolves a script path relative to a global or workflow config base path.
    /// </summary>
    public static ScriptWorkspaceResult<string> ResolveConfigRelativeScriptPath(
        string? workflowName,
        string relativePath)
    {
        var basePath = string.IsNullOrWhiteSpace(workflowName)
            ? string.Empty
            : ScriptWorkflowWorkspaceLayout.WorkflowPath(workflowName.Trim());

        return ScriptWorkspacePath.ResolveRelative(basePath, relativePath);
    }

    /// <summary>
    /// Returns Lua module probe paths in active-workflow then global order.
    /// </summary>
    public static ScriptWorkspaceResult<IReadOnlyList<string>> GetLuaModuleProbePaths(
        string moduleName,
        string? activeWorkflowName = null)
    {
        var fileName = NormalizeModuleFileName(moduleName, ".lua");
        if (!fileName.Success)
        {
            return ScriptWorkspaceResult<IReadOnlyList<string>>.Failed(fileName.Error!);
        }

        var candidates = new List<string>();
        var workflowName = TrimToNull(activeWorkflowName);
        if (workflowName != null)
        {
            candidates.Add(ScriptWorkflowWorkspaceLayout.Combine(
                ScriptWorkflowWorkspaceLayout.WorkflowScriptsPath(workflowName),
                fileName.Value));
            candidates.Add(ScriptWorkflowWorkspaceLayout.Combine(
                ScriptWorkflowWorkspaceLayout.WorkflowLibrariesPath(workflowName),
                fileName.Value));
        }

        candidates.Add(ScriptWorkflowWorkspaceLayout.Combine(
            ScriptWorkflowWorkspaceLayout.LibrariesDirectory,
            fileName.Value));
        candidates.Add(ScriptWorkflowWorkspaceLayout.Combine(
            ScriptWorkflowWorkspaceLayout.ScriptsDirectory,
            fileName.Value));

        return ScriptWorkspaceResult<IReadOnlyList<string>>.Succeeded(candidates);
    }

    /// <summary>
    /// Returns JavaScript module probe paths for an import specifier.
    /// </summary>
    public static ScriptWorkspaceResult<IReadOnlyList<string>> GetJavaScriptModuleProbePaths(
        string importerPath,
        string specifier)
    {
        var normalizedImporter = ScriptWorkspacePath.Normalize(importerPath);
        if (!normalizedImporter.Success)
        {
            return ScriptWorkspaceResult<IReadOnlyList<string>>.Failed(normalizedImporter.Error!);
        }

        if (string.IsNullOrWhiteSpace(specifier))
        {
            return Invalid(specifier ?? string.Empty, "Module specifier cannot be empty.");
        }

        var importerDirectory = GetDirectoryName(normalizedImporter.Value ?? string.Empty);
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(importerDirectory))
        {
            var importerRelative = ScriptWorkspacePath.ResolveRelative(importerDirectory, specifier);
            if (importerRelative.Success)
            {
                candidates.Add(importerRelative.Value!);
            }
            else if (IsUnsafeSpecifier(importerRelative.Error))
            {
                return ScriptWorkspaceResult<IReadOnlyList<string>>.Failed(importerRelative.Error!);
            }
        }

        var rootRelative = ScriptWorkspacePath.ResolveRelative(string.Empty, specifier);
        if (rootRelative.Success)
        {
            if (!candidates.Contains(rootRelative.Value!, StringComparer.Ordinal))
            {
                candidates.Add(rootRelative.Value!);
            }
        }
        else if (candidates.Count == 0 && IsUnsafeSpecifier(rootRelative.Error))
        {
            return ScriptWorkspaceResult<IReadOnlyList<string>>.Failed(rootRelative.Error!);
        }

        return ScriptWorkspaceResult<IReadOnlyList<string>>.Succeeded(candidates);
    }

    private static ScriptWorkspaceResult<string> NormalizeModuleFileName(string moduleName, string extension)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            return InvalidString(moduleName ?? string.Empty, "Module name cannot be empty.");
        }

        var fileName = moduleName.Trim();
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += extension;
        }

        return ScriptWorkspacePath.Normalize(fileName);
    }

    private static string GetDirectoryName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static bool IsUnsafeSpecifier(ScriptWorkspaceError? error) =>
        error?.Code == ScriptWorkspaceErrorCode.InvalidPath &&
        (error.Message.Contains("drive-rooted", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("URI", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("NUL", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("escape", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("relative", StringComparison.OrdinalIgnoreCase));

    private static ScriptWorkspaceResult<IReadOnlyList<string>> Invalid(string path, string message) =>
        ScriptWorkspaceResult<IReadOnlyList<string>>.Failed(new ScriptWorkspaceError(
            ScriptWorkspaceErrorCode.InvalidPath,
            path,
            message));

    private static ScriptWorkspaceResult<string> InvalidString(string path, string message) =>
        ScriptWorkspaceResult<string>.Failed(new ScriptWorkspaceError(
            ScriptWorkspaceErrorCode.InvalidPath,
            path,
            message));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
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
