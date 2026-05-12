namespace Guida.Scripting;

/// <summary>
/// Workflow workspace activation options.
/// </summary>
public sealed record ScriptWorkflowWorkspaceActivationOptions
{
    /// <summary>
    /// Whether disabled workflows may be activated.
    /// </summary>
    public bool AllowDisabled { get; init; }
}

/// <summary>
/// Workflow workspace creation options.
/// </summary>
public sealed record ScriptWorkflowWorkspaceCreateOptions
{
    /// <summary>
    /// Whether the new workflow starts enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Optional manifest summary.
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Workflow workspace import options.
/// </summary>
public sealed record ScriptWorkflowWorkspaceImportOptions
{
    /// <summary>
    /// Target workflow name. Defaults to the exported workflow name.
    /// </summary>
    public string? WorkflowName { get; init; }

    /// <summary>
    /// Whether existing files in the target workflow may be overwritten.
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>
/// Read-model entry for one workflow folder.
/// </summary>
public sealed record ScriptWorkflowWorkspaceReadModel
{
    public string? ActiveWorkflowName { get; init; }
    public IReadOnlyList<ScriptWorkflowWorkspaceWorkflowReadModel> Workflows { get; init; } =
        Array.Empty<ScriptWorkflowWorkspaceWorkflowReadModel>();
}

/// <summary>
/// Workflow read-model summary derived from workspace discovery.
/// </summary>
public sealed record ScriptWorkflowWorkspaceWorkflowReadModel
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public bool IsActive { get; init; }
    public bool HasEvents { get; init; }
    public bool HasWorkers { get; init; }
    public bool HasTools { get; init; }
    public bool HasViews { get; init; }
    public bool HasLedgerSchema { get; init; }
    public int ScriptCount { get; init; }
    public int LibraryCount { get; init; }
    public int ViewCount { get; init; }
    public int ConfigFileCount { get; init; }
    public string? ManifestError { get; init; }
}

/// <summary>
/// Detailed read-only workflow inspection model.
/// </summary>
public sealed record ScriptWorkflowWorkspaceInspection
{
    public ScriptWorkflowDefinition Workflow { get; init; } = new();
    public ScriptWorkflowWorkspaceWorkflowReadModel Summary { get; init; } = new();
    public ScriptWorkflowWorkspaceOverlay Overlay { get; init; } = new();
    public IReadOnlyList<ScriptWorkflowWorkspaceFile> Files { get; init; } =
        Array.Empty<ScriptWorkflowWorkspaceFile>();
}

/// <summary>
/// Workflow workspace summary.
/// </summary>
public sealed record ScriptWorkflowWorkspaceSummary
{
    public string? ActiveWorkflowName { get; init; }
    public int WorkflowCount { get; init; }
    public int EnabledWorkflowCount { get; init; }
    public int DisabledWorkflowCount { get; init; }
    public int WorkflowWithEventsCount { get; init; }
    public int WorkflowWithWorkersCount { get; init; }
    public int WorkflowWithToolsCount { get; init; }
    public int WorkflowWithViewsCount { get; init; }
    public int WorkflowWithLedgerSchemaCount { get; init; }
    public int TotalScriptCount { get; init; }
    public int TotalLibraryCount { get; init; }
    public int TotalViewCount { get; init; }
}

/// <summary>
/// Result of activating or deactivating a workflow overlay.
/// </summary>
public sealed record ScriptWorkflowWorkspaceActivation
{
    public string? ActiveWorkflowName { get; init; }
    public string? ActiveWorkflowPath { get; init; }
    public ScriptWorkflowWorkspaceOverlay Overlay { get; init; } = new();
}

/// <summary>
/// Portable workflow folder export.
/// </summary>
public sealed record ScriptWorkflowWorkspaceExport
{
    public int SchemaVersion { get; init; } = 1;
    public string WorkflowName { get; init; } = string.Empty;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ScriptWorkflowWorkspaceExportFile> Files { get; init; } =
        Array.Empty<ScriptWorkflowWorkspaceExportFile>();
}

/// <summary>
/// One exported workflow file.
/// </summary>
public sealed record ScriptWorkflowWorkspaceExportFile
{
    public string Path { get; init; } = string.Empty;
    public byte[] Content { get; init; } = Array.Empty<byte>();
    public DateTimeOffset? LastModifiedAt { get; init; }
}

/// <summary>
/// Result of importing a portable workflow payload.
/// </summary>
public sealed record ScriptWorkflowWorkspaceImportResult
{
    public string WorkflowName { get; init; } = string.Empty;
    public string WorkflowPath { get; init; } = string.Empty;
    public int ImportedFileCount { get; init; }
}
