using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Guida.Scripting;

/// <summary>
/// Default workflow workspace management and read-model implementation.
/// </summary>
public sealed class ScriptWorkflowWorkspaceManager : IScriptWorkflowWorkspaceManager
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IScriptWorkflowWorkspaceDiscovery _discovery;
    private readonly object _gate = new();
    private string? _activeWorkflowName;

    /// <summary>
    /// Creates a workflow workspace manager.
    /// </summary>
    public ScriptWorkflowWorkspaceManager(IScriptWorkflowWorkspaceDiscovery? discovery = null)
    {
        _discovery = discovery ?? new ScriptWorkflowWorkspaceDiscovery();
    }

    /// <inheritdoc />
    public string? ActiveWorkflowName
    {
        get
        {
            lock (_gate)
            {
                return _activeWorkflowName;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ScriptWorkflowWorkspaceActivation>? ActiveWorkflowChanged;

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceReadModel>> ListAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var snapshot = await _discovery.DiscoverAsync(workspace, options, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceReadModel>.Failed(snapshot.Error!);
        }

        var activeWorkflowName = ActiveWorkflowName;
        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceReadModel>.Succeeded(
            new ScriptWorkflowWorkspaceReadModel
            {
                ActiveWorkflowName = activeWorkflowName,
                Workflows = snapshot.Value!.Workflows
                    .Select(workflow => ToReadModel(workflow, activeWorkflowName))
                    .ToArray()
            });
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>> InspectAsync(
        IScriptWorkspace workspace,
        string workflowName,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var normalizedName = NormalizeWorkflowName(workflowName);
        if (!normalizedName.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>.Failed(normalizedName.Error!);
        }

        var snapshot = await _discovery.DiscoverAsync(workspace, options, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>.Failed(snapshot.Error!);
        }

        var workflow = snapshot.Value!.GetWorkflow(normalizedName.Value!);
        if (workflow == null)
        {
            return Failed<ScriptWorkflowWorkspaceInspection>(
                ScriptWorkflowWorkspaceErrorCode.NotFound,
                ScriptWorkflowWorkspaceLayout.WorkflowPath(normalizedName.Value!),
                $"Workflow '{normalizedName.Value}' was not found.");
        }

        var files = await ListWorkflowFilesAsync(workspace, workflow.Name, cancellationToken).ConfigureAwait(false);
        if (!files.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>.Failed(files.Error!);
        }

        var activeWorkflowName = ActiveWorkflowName;
        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceInspection>.Succeeded(
            new ScriptWorkflowWorkspaceInspection
            {
                Workflow = workflow,
                Summary = ToReadModel(workflow, activeWorkflowName),
                Overlay = snapshot.Value.CreateOverlay(workflow.Name),
                Files = files.Value!
            });
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSummary>> SummarizeAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var snapshot = await _discovery.DiscoverAsync(workspace, options, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSummary>.Failed(snapshot.Error!);
        }

        var workflows = snapshot.Value!.Workflows;
        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSummary>.Succeeded(
            new ScriptWorkflowWorkspaceSummary
            {
                ActiveWorkflowName = ActiveWorkflowName,
                WorkflowCount = workflows.Count,
                EnabledWorkflowCount = workflows.Count(workflow => workflow.Enabled),
                DisabledWorkflowCount = workflows.Count(workflow => !workflow.Enabled),
                WorkflowWithEventsCount = workflows.Count(workflow => workflow.HasEvents),
                WorkflowWithWorkersCount = workflows.Count(workflow => workflow.HasWorkers),
                WorkflowWithToolsCount = workflows.Count(workflow => workflow.HasTools),
                WorkflowWithViewsCount = workflows.Count(workflow => workflow.HasViews),
                WorkflowWithLedgerSchemaCount = workflows.Count(workflow => workflow.HasLedgerSchema),
                TotalScriptCount = workflows.Sum(workflow => workflow.ScriptCount),
                TotalLibraryCount = workflows.Sum(workflow => workflow.Libraries.Count),
                TotalViewCount = workflows.Sum(workflow => workflow.Views.Count)
            });
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceActivation>> ActivateAsync(
        IScriptWorkspace workspace,
        string? workflowName,
        ScriptWorkflowWorkspaceActivationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var activationOptions = options ?? new ScriptWorkflowWorkspaceActivationOptions();
        var requestedName = TrimToNull(workflowName);
        var snapshot = await _discovery.DiscoverAsync(workspace, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceActivation>.Failed(snapshot.Error!);
        }

        ScriptWorkflowDefinition? workflow = null;
        if (requestedName != null)
        {
            var normalizedName = NormalizeWorkflowName(requestedName);
            if (!normalizedName.Success)
            {
                return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceActivation>.Failed(normalizedName.Error!);
            }

            workflow = snapshot.Value!.GetWorkflow(normalizedName.Value!);
            if (workflow == null)
            {
                return Failed<ScriptWorkflowWorkspaceActivation>(
                    ScriptWorkflowWorkspaceErrorCode.NotFound,
                    ScriptWorkflowWorkspaceLayout.WorkflowPath(normalizedName.Value!),
                    $"Workflow '{normalizedName.Value}' was not found.");
            }

            if (!workflow.Enabled && !activationOptions.AllowDisabled)
            {
                return Failed<ScriptWorkflowWorkspaceActivation>(
                    ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                    workflow.Path,
                    $"Workflow '{workflow.Name}' is disabled.");
            }
        }

        var activation = new ScriptWorkflowWorkspaceActivation
        {
            ActiveWorkflowName = workflow?.Name,
            ActiveWorkflowPath = workflow?.Path,
            Overlay = snapshot.Value!.CreateOverlay(workflow?.Name)
        };

        var changed = SetActiveWorkflowName(workflow?.Name);
        if (changed)
        {
            ActiveWorkflowChanged?.Invoke(this, activation);
        }

        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceActivation>.Succeeded(activation);
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>> CreateAsync(
        IScriptWorkspace workspace,
        string workflowName,
        ScriptWorkflowWorkspaceCreateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var normalizedName = NormalizeWorkflowName(workflowName);
        if (!normalizedName.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>.Failed(normalizedName.Error!);
        }

        var path = ScriptWorkflowWorkspaceLayout.WorkflowPath(normalizedName.Value!);
        var existing = await workspace.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing.Success)
        {
            return Failed<ScriptWorkflowDefinition>(
                ScriptWorkflowWorkspaceErrorCode.AlreadyExists,
                path,
                $"Workflow '{normalizedName.Value}' already exists.");
        }

        if (existing.Error?.Code != ScriptWorkspaceErrorCode.NotFound)
        {
            return Failed<ScriptWorkflowDefinition>(existing.Error!);
        }

        var createOptions = options ?? new ScriptWorkflowWorkspaceCreateOptions();
        var manifest = new JsonObject
        {
            ["enabled"] = createOptions.Enabled
        };
        if (!string.IsNullOrWhiteSpace(createOptions.Summary))
        {
            manifest["summary"] = createOptions.Summary;
        }

        var write = await workspace.WriteFileAsync(
            ScriptWorkflowWorkspaceLayout.WorkflowFilePath(normalizedName.Value!, ScriptWorkflowWorkspaceLayout.WorkflowManifestFile),
            Encoding.UTF8.GetBytes(manifest.ToJsonString(ManifestJsonOptions)),
            new ScriptWorkspaceWriteOptions { CreateDirectories = true, Overwrite = false },
            cancellationToken).ConfigureAwait(false);
        if (!write.Success)
        {
            return Failed<ScriptWorkflowDefinition>(write.Error!);
        }

        return GetWorkflowAfterMutation(
            await _discovery.DiscoverAsync(workspace, cancellationToken: cancellationToken).ConfigureAwait(false),
            normalizedName.Value!);
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>> SetEnabledAsync(
        IScriptWorkspace workspace,
        string workflowName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var normalizedName = NormalizeWorkflowName(workflowName);
        if (!normalizedName.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>.Failed(normalizedName.Error!);
        }

        var snapshot = await _discovery.DiscoverAsync(workspace, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>.Failed(snapshot.Error!);
        }

        var workflow = snapshot.Value!.GetWorkflow(normalizedName.Value!);
        if (workflow == null)
        {
            return Failed<ScriptWorkflowDefinition>(
                ScriptWorkflowWorkspaceErrorCode.NotFound,
                ScriptWorkflowWorkspaceLayout.WorkflowPath(normalizedName.Value!),
                $"Workflow '{normalizedName.Value}' was not found.");
        }

        var manifestPath = ScriptWorkflowWorkspaceLayout.WorkflowFilePath(
            workflow.Name,
            ScriptWorkflowWorkspaceLayout.WorkflowManifestFile);
        var manifest = new JsonObject();
        var existing = await workspace.ReadFileAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (existing.Success)
        {
            try
            {
                manifest = JsonNode.Parse(existing.Value!.Content.Span) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                manifest = new JsonObject();
            }
        }
        else if (existing.Error?.Code != ScriptWorkspaceErrorCode.NotFound)
        {
            return Failed<ScriptWorkflowDefinition>(existing.Error!);
        }

        manifest["enabled"] = enabled;
        var write = await workspace.WriteFileAsync(
            manifestPath,
            Encoding.UTF8.GetBytes(manifest.ToJsonString(ManifestJsonOptions)),
            new ScriptWorkspaceWriteOptions { CreateDirectories = true },
            cancellationToken).ConfigureAwait(false);
        if (!write.Success)
        {
            return Failed<ScriptWorkflowDefinition>(write.Error!);
        }

        if (!enabled && string.Equals(ActiveWorkflowName, workflow.Name, StringComparison.OrdinalIgnoreCase))
        {
            await ActivateAsync(workspace, null, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return GetWorkflowAfterMutation(
            await _discovery.DiscoverAsync(workspace, cancellationToken: cancellationToken).ConfigureAwait(false),
            workflow.Name);
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>> ExportAsync(
        IScriptWorkspace workspace,
        string workflowName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var normalizedName = NormalizeWorkflowName(workflowName);
        if (!normalizedName.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>.Failed(normalizedName.Error!);
        }

        var snapshot = await _discovery.DiscoverAsync(workspace, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>.Failed(snapshot.Error!);
        }

        var workflow = snapshot.Value!.GetWorkflow(normalizedName.Value!);
        if (workflow == null)
        {
            return Failed<ScriptWorkflowWorkspaceExport>(
                ScriptWorkflowWorkspaceErrorCode.NotFound,
                ScriptWorkflowWorkspaceLayout.WorkflowPath(normalizedName.Value!),
                $"Workflow '{normalizedName.Value}' was not found.");
        }

        var files = await ListWorkflowFilesAsync(workspace, workflow.Name, cancellationToken).ConfigureAwait(false);
        if (!files.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>.Failed(files.Error!);
        }

        var exportedFiles = new List<ScriptWorkflowWorkspaceExportFile>();
        foreach (var file in files.Value!)
        {
            var content = await workspace.ReadFileAsync(file.Path, cancellationToken).ConfigureAwait(false);
            if (!content.Success)
            {
                return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>.Failed(
                    new ScriptWorkflowWorkspaceError(
                        ScriptWorkflowWorkspaceErrorCode.WorkspaceError,
                        file.Path,
                        content.Error!.Message,
                        content.Error));
            }

            exportedFiles.Add(new ScriptWorkflowWorkspaceExportFile
            {
                Path = ToWorkflowRelativePath(workflow.Path, file.Path),
                Content = content.Value!.Content.ToArray(),
                LastModifiedAt = file.LastModifiedAt
            });
        }

        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceExport>.Succeeded(
            new ScriptWorkflowWorkspaceExport
            {
                WorkflowName = workflow.Name,
                Files = new ReadOnlyCollection<ScriptWorkflowWorkspaceExportFile>(exportedFiles)
            });
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceImportResult>> ImportAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceExport export,
        ScriptWorkflowWorkspaceImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(export);

        if (export.SchemaVersion != 1)
        {
            return Failed<ScriptWorkflowWorkspaceImportResult>(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                string.Empty,
                $"Workflow export schema version {export.SchemaVersion} is not supported.");
        }

        var importOptions = options ?? new ScriptWorkflowWorkspaceImportOptions();
        var targetName = NormalizeWorkflowName(importOptions.WorkflowName ?? export.WorkflowName);
        if (!targetName.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceImportResult>.Failed(targetName.Error!);
        }

        if (export.Files.Count == 0)
        {
            return Failed<ScriptWorkflowWorkspaceImportResult>(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                targetName.Value!,
                "Workflow export does not contain any files.");
        }

        var workflowPath = ScriptWorkflowWorkspaceLayout.WorkflowPath(targetName.Value!);
        var existing = await workspace.GetEntryAsync(workflowPath, cancellationToken).ConfigureAwait(false);
        if (existing.Success && !importOptions.OverwriteExisting)
        {
            return Failed<ScriptWorkflowWorkspaceImportResult>(
                ScriptWorkflowWorkspaceErrorCode.AlreadyExists,
                workflowPath,
                $"Workflow '{targetName.Value}' already exists.");
        }

        if (!existing.Success && existing.Error?.Code != ScriptWorkspaceErrorCode.NotFound)
        {
            return Failed<ScriptWorkflowWorkspaceImportResult>(existing.Error!);
        }

        var importedFileCount = 0;
        foreach (var file in export.Files)
        {
            var resolved = ResolveExportFilePath(targetName.Value!, file.Path);
            if (!resolved.Success)
            {
                return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceImportResult>.Failed(resolved.Error!);
            }

            var write = await workspace.WriteFileAsync(
                resolved.Value!,
                file.Content,
                new ScriptWorkspaceWriteOptions
                {
                    CreateDirectories = true,
                    Overwrite = importOptions.OverwriteExisting
                },
                cancellationToken).ConfigureAwait(false);
            if (!write.Success)
            {
                return Failed<ScriptWorkflowWorkspaceImportResult>(write.Error!);
            }

            importedFileCount++;
        }

        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceImportResult>.Succeeded(
            new ScriptWorkflowWorkspaceImportResult
            {
                WorkflowName = targetName.Value!,
                WorkflowPath = workflowPath,
                ImportedFileCount = importedFileCount
            });
    }

    private static ScriptWorkflowWorkspaceWorkflowReadModel ToReadModel(
        ScriptWorkflowDefinition workflow,
        string? activeWorkflowName) =>
        new()
        {
            Name = workflow.Name,
            Path = workflow.Path,
            Enabled = workflow.Enabled,
            IsActive = string.Equals(workflow.Name, activeWorkflowName, StringComparison.OrdinalIgnoreCase),
            HasEvents = workflow.HasEvents,
            HasWorkers = workflow.HasWorkers,
            HasTools = workflow.HasTools,
            HasViews = workflow.HasViews,
            HasLedgerSchema = workflow.HasLedgerSchema,
            ScriptCount = workflow.ScriptCount,
            LibraryCount = workflow.Libraries.Count,
            ViewCount = workflow.Views.Count,
            ConfigFileCount = workflow.ConfigFiles.Count,
            ManifestError = workflow.ManifestError
        };

    private async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>> ListWorkflowFilesAsync(
        IScriptWorkspace workspace,
        string workflowName,
        CancellationToken cancellationToken)
    {
        var workflowPath = ScriptWorkflowWorkspaceLayout.WorkflowPath(workflowName);
        var files = new List<ScriptWorkflowWorkspaceFile>();
        var stack = new Stack<string>();
        stack.Push(workflowPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = stack.Pop();
            var entries = await workspace.ListAsync(currentPath, cancellationToken).ConfigureAwait(false);
            if (!entries.Success)
            {
                return Failed<IReadOnlyList<ScriptWorkflowWorkspaceFile>>(entries.Error!);
            }

            foreach (var entry in entries.Value!.OrderByDescending(entry => entry.Path, StringComparer.Ordinal))
            {
                if (entry.Kind == ScriptWorkspaceEntryKind.Directory)
                {
                    stack.Push(entry.Path);
                    continue;
                }

                files.Add(new ScriptWorkflowWorkspaceFile
                {
                    Path = entry.Path,
                    Name = entry.Name,
                    NameWithoutExtension = GetNameWithoutExtension(entry.Name),
                    Scope = ScriptWorkflowWorkspaceScope.Workflow,
                    WorkflowName = workflowName,
                    Length = entry.Length,
                    LastModifiedAt = entry.LastModifiedAt
                });
            }
        }

        return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>.Succeeded(
            new ReadOnlyCollection<ScriptWorkflowWorkspaceFile>(
                files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray()));
    }

    private bool SetActiveWorkflowName(string? workflowName)
    {
        lock (_gate)
        {
            if (string.Equals(_activeWorkflowName, workflowName, StringComparison.OrdinalIgnoreCase))
            {
                _activeWorkflowName = workflowName;
                return false;
            }

            _activeWorkflowName = workflowName;
            return true;
        }
    }

    private static ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition> GetWorkflowAfterMutation(
        ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot> snapshot,
        string workflowName)
    {
        if (!snapshot.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>.Failed(snapshot.Error!);
        }

        var workflow = snapshot.Value!.GetWorkflow(workflowName);
        return workflow == null
            ? Failed<ScriptWorkflowDefinition>(
                ScriptWorkflowWorkspaceErrorCode.NotFound,
                ScriptWorkflowWorkspaceLayout.WorkflowPath(workflowName),
                $"Workflow '{workflowName}' was not found after the update.")
            : ScriptWorkflowWorkspaceResult<ScriptWorkflowDefinition>.Succeeded(workflow);
    }

    private static string ToWorkflowRelativePath(string workflowPath, string filePath)
    {
        if (string.Equals(workflowPath, filePath, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return filePath[(workflowPath.Length + 1)..];
    }

    private static ScriptWorkflowWorkspaceResult<string> ResolveExportFilePath(
        string workflowName,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return FailedString(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, relativePath ?? string.Empty, "Export file path cannot be empty.");
        }

        var workflowPath = ScriptWorkflowWorkspaceLayout.WorkflowPath(workflowName);
        var resolved = ScriptWorkspacePath.ResolveRelative(workflowPath, relativePath);
        if (!resolved.Success)
        {
            return ScriptWorkflowWorkspaceResult<string>.Failed(new ScriptWorkflowWorkspaceError(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                relativePath,
                resolved.Error!.Message,
                resolved.Error));
        }

        if (!resolved.Value!.StartsWith(workflowPath + "/", StringComparison.Ordinal))
        {
            return FailedString(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                relativePath,
                "Export file path must stay inside the target workflow.");
        }

        return ScriptWorkflowWorkspaceResult<string>.Succeeded(resolved.Value);
    }

    private static ScriptWorkflowWorkspaceResult<string> NormalizeWorkflowName(string workflowName)
    {
        var name = TrimToNull(workflowName);
        if (name == null)
        {
            return FailedString(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, string.Empty, "Workflow name cannot be empty.");
        }

        if (name.Contains('/') || name.Contains('\\'))
        {
            return FailedString(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, name, "Workflow name must be a single path segment.");
        }

        var normalized = ScriptWorkspacePath.Normalize(name);
        if (!normalized.Success)
        {
            return ScriptWorkflowWorkspaceResult<string>.Failed(new ScriptWorkflowWorkspaceError(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                name,
                normalized.Error!.Message,
                normalized.Error));
        }

        if (string.IsNullOrEmpty(normalized.Value))
        {
            return FailedString(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, name, "Workflow name cannot be empty.");
        }

        return ScriptWorkflowWorkspaceResult<string>.Succeeded(normalized.Value);
    }

    private static string GetNameWithoutExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ScriptWorkflowWorkspaceResult<T> Failed<T>(ScriptWorkspaceError error) =>
        ScriptWorkflowWorkspaceResult<T>.Failed(new ScriptWorkflowWorkspaceError(
            ScriptWorkflowWorkspaceErrorCode.WorkspaceError,
            error.Path,
            error.Message,
            error));

    private static ScriptWorkflowWorkspaceResult<T> Failed<T>(
        ScriptWorkflowWorkspaceErrorCode code,
        string path,
        string message) =>
        ScriptWorkflowWorkspaceResult<T>.Failed(new ScriptWorkflowWorkspaceError(code, path, message));

    private static ScriptWorkflowWorkspaceResult<string> FailedString(
        ScriptWorkflowWorkspaceErrorCode code,
        string path,
        string message) =>
        ScriptWorkflowWorkspaceResult<string>.Failed(new ScriptWorkflowWorkspaceError(code, path, message));
}
