using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Guida.Scripting;

/// <summary>
/// Default workflow workspace layout discovery over <see cref="IScriptWorkspace" />.
/// </summary>
public sealed class ScriptWorkflowWorkspaceDiscovery : IScriptWorkflowWorkspaceDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>> DiscoverAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        options ??= new ScriptWorkflowWorkspaceDiscoveryOptions();
        var extensions = NormalizeExtensions(options.ScriptExtensions);
        if (extensions.Count == 0)
        {
            return Failed<ScriptWorkflowWorkspaceSnapshot>(
                ScriptWorkflowWorkspaceErrorCode.InvalidRequest,
                string.Empty,
                "At least one script extension is required.");
        }

        var globalScripts = await ListFilesRecursive(
            workspace,
            ScriptWorkflowWorkspaceLayout.ScriptsDirectory,
            extensions,
            ScriptWorkflowWorkspaceScope.Global,
            workflowName: null,
            cancellationToken).ConfigureAwait(false);
        if (!globalScripts.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Failed(globalScripts.Error!);
        }

        var globalLibraries = await ListFilesRecursive(
            workspace,
            ScriptWorkflowWorkspaceLayout.LibrariesDirectory,
            extensions,
            ScriptWorkflowWorkspaceScope.Global,
            workflowName: null,
            cancellationToken).ConfigureAwait(false);
        if (!globalLibraries.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Failed(globalLibraries.Error!);
        }

        var globalViews = await ListViews(
            workspace,
            ScriptWorkflowWorkspaceLayout.ViewsDirectory,
            ScriptWorkflowWorkspaceScope.Global,
            workflowName: null,
            cancellationToken).ConfigureAwait(false);
        if (!globalViews.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Failed(globalViews.Error!);
        }

        var globalConfigs = await ListConfigFiles(
            workspace,
            workflowName: null,
            basePath: string.Empty,
            includeWorkflowOnlyFiles: false,
            cancellationToken).ConfigureAwait(false);
        if (!globalConfigs.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Failed(globalConfigs.Error!);
        }

        var workflows = await ListWorkflows(workspace, extensions, cancellationToken).ConfigureAwait(false);
        if (!workflows.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Failed(workflows.Error!);
        }

        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceSnapshot>.Succeeded(
            new ScriptWorkflowWorkspaceSnapshot
            {
                GlobalScripts = globalScripts.Value!,
                GlobalLibraries = globalLibraries.Value!,
                GlobalViews = globalViews.Value!,
                GlobalConfigFiles = globalConfigs.Value!,
                Workflows = workflows.Value!
            });
    }

    /// <inheritdoc />
    public async Task<ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceLedgerSchemas>> LoadLedgerSchemasAsync(
        IScriptWorkspace workspace,
        ScriptWorkflowWorkspaceDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();

        var discovery = await DiscoverAsync(workspace, options, cancellationToken).ConfigureAwait(false);
        if (!discovery.Success)
        {
            return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceLedgerSchemas>.Failed(discovery.Error!);
        }

        var schemaJsonByWorkflow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workflow in discovery.Value!.Workflows.Where(workflow => workflow.HasLedgerSchema))
        {
            var schemaPath = ScriptWorkflowWorkspaceLayout.WorkflowFilePath(
                workflow.Name,
                ScriptWorkflowWorkspaceLayout.WorkflowLedgerSchemaFile);
            var schema = await workspace.ReadFileAsync(schemaPath, cancellationToken).ConfigureAwait(false);
            if (!schema.Success)
            {
                return Failed<ScriptWorkflowWorkspaceLedgerSchemas>(schema.Error!);
            }

            schemaJsonByWorkflow[workflow.Name] = Encoding.UTF8.GetString(schema.Value!.Content.ToArray());
        }

        var readOnly = new ReadOnlyDictionary<string, string>(schemaJsonByWorkflow);
        return ScriptWorkflowWorkspaceResult<ScriptWorkflowWorkspaceLedgerSchemas>.Succeeded(
            new ScriptWorkflowWorkspaceLedgerSchemas
            {
                SchemaJsonByWorkflow = readOnly,
                Validator = ScriptWorkflowLedgerSchemaValidator.FromJsonByWorkflow(readOnly)
            });
    }

    private static async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>> ListWorkflows(
        IScriptWorkspace workspace,
        IReadOnlySet<string> extensions,
        CancellationToken cancellationToken)
    {
        var workflowDirs = await ListExistingDirectory(workspace, ScriptWorkflowWorkspaceLayout.WorkflowsDirectory, cancellationToken).ConfigureAwait(false);
        if (!workflowDirs.Success)
        {
            return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Failed(workflowDirs.Error!);
        }

        var workflows = new List<ScriptWorkflowDefinition>();
        foreach (var workflowDir in workflowDirs.Value!
            .Where(entry => entry.Kind == ScriptWorkspaceEntryKind.Directory)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var workflowName = workflowDir.Name;
            var workflowPath = workflowDir.Path;
            var scripts = await ListFilesRecursive(
                workspace,
                ScriptWorkflowWorkspaceLayout.WorkflowScriptsPath(workflowName),
                extensions,
                ScriptWorkflowWorkspaceScope.Workflow,
                workflowName,
                cancellationToken).ConfigureAwait(false);
            if (!scripts.Success)
            {
                return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Failed(scripts.Error!);
            }

            var libraries = await ListFilesRecursive(
                workspace,
                ScriptWorkflowWorkspaceLayout.WorkflowLibrariesPath(workflowName),
                extensions,
                ScriptWorkflowWorkspaceScope.Workflow,
                workflowName,
                cancellationToken).ConfigureAwait(false);
            if (!libraries.Success)
            {
                return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Failed(libraries.Error!);
            }

            var views = await ListViews(
                workspace,
                ScriptWorkflowWorkspaceLayout.WorkflowViewsPath(workflowName),
                ScriptWorkflowWorkspaceScope.Workflow,
                workflowName,
                cancellationToken).ConfigureAwait(false);
            if (!views.Success)
            {
                return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Failed(views.Error!);
            }

            var configs = await ListConfigFiles(
                workspace,
                workflowName,
                workflowPath,
                includeWorkflowOnlyFiles: true,
                cancellationToken).ConfigureAwait(false);
            if (!configs.Success)
            {
                return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Failed(configs.Error!);
            }

            var manifest = await ReadManifest(workspace, workflowName, cancellationToken).ConfigureAwait(false);
            workflows.Add(new ScriptWorkflowDefinition
            {
                Name = workflowName,
                Path = workflowPath,
                Enabled = manifest.Enabled,
                HasEvents = HasConfig(configs.Value!, ScriptWorkflowWorkspaceConfigKind.Events),
                HasWorkers = HasConfig(configs.Value!, ScriptWorkflowWorkspaceConfigKind.Queues),
                HasTools = HasConfig(configs.Value!, ScriptWorkflowWorkspaceConfigKind.Tools),
                HasViews = views.Value!.Count > 0,
                HasLedgerSchema = HasConfig(configs.Value!, ScriptWorkflowWorkspaceConfigKind.LedgerSchema),
                ScriptCount = scripts.Value!.Count,
                ManifestError = manifest.Error,
                Scripts = scripts.Value,
                Libraries = libraries.Value!,
                Views = views.Value,
                ConfigFiles = configs.Value!
            });
        }

        return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowDefinition>>.Succeeded(
            new ReadOnlyCollection<ScriptWorkflowDefinition>(workflows));
    }

    private static async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>> ListFilesRecursive(
        IScriptWorkspace workspace,
        string path,
        IReadOnlySet<string> extensions,
        ScriptWorkflowWorkspaceScope scope,
        string? workflowName,
        CancellationToken cancellationToken)
    {
        var entries = await ListExistingDirectory(workspace, path, cancellationToken).ConfigureAwait(false);
        if (!entries.Success)
        {
            return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>.Failed(entries.Error!);
        }

        var files = new List<ScriptWorkflowWorkspaceFile>();
        foreach (var entry in entries.Value!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Kind == ScriptWorkspaceEntryKind.Directory)
            {
                var nested = await ListFilesRecursive(
                    workspace,
                    entry.Path,
                    extensions,
                    scope,
                    workflowName,
                    cancellationToken).ConfigureAwait(false);
                if (!nested.Success)
                {
                    return nested;
                }

                files.AddRange(nested.Value!);
                continue;
            }

            if (extensions.Contains(GetExtension(entry.Name)))
            {
                files.Add(ToWorkspaceFile(entry, scope, workflowName));
            }
        }

        return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>.Succeeded(
            new ReadOnlyCollection<ScriptWorkflowWorkspaceFile>(
                files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray()));
    }

    private static async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>> ListViews(
        IScriptWorkspace workspace,
        string path,
        ScriptWorkflowWorkspaceScope scope,
        string? workflowName,
        CancellationToken cancellationToken)
    {
        var entries = await ListExistingDirectory(workspace, path, cancellationToken).ConfigureAwait(false);
        if (!entries.Success)
        {
            return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>.Failed(entries.Error!);
        }

        var views = entries.Value!
            .Where(entry => entry.Kind == ScriptWorkspaceEntryKind.File)
            .Where(entry => string.Equals(GetExtension(entry.Name), ".json", StringComparison.Ordinal))
            .Where(entry => !entry.Name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => ToWorkspaceFile(entry, scope, workflowName))
            .ToArray();

        return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceFile>>.Succeeded(views);
    }

    private static async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceConfigFile>>> ListConfigFiles(
        IScriptWorkspace workspace,
        string? workflowName,
        string basePath,
        bool includeWorkflowOnlyFiles,
        CancellationToken cancellationToken)
    {
        var scope = workflowName == null ? ScriptWorkflowWorkspaceScope.Global : ScriptWorkflowWorkspaceScope.Workflow;
        var candidates = new List<(string FileName, ScriptWorkflowWorkspaceConfigKind Kind)>
        {
            (ScriptWorkflowWorkspaceLayout.EventsConfigFile, ScriptWorkflowWorkspaceConfigKind.Events),
            (ScriptWorkflowWorkspaceLayout.QueuesConfigFile, ScriptWorkflowWorkspaceConfigKind.Queues),
            (ScriptWorkflowWorkspaceLayout.ToolsConfigFile, ScriptWorkflowWorkspaceConfigKind.Tools)
        };
        if (includeWorkflowOnlyFiles)
        {
            candidates.Add((ScriptWorkflowWorkspaceLayout.WorkflowManifestFile, ScriptWorkflowWorkspaceConfigKind.Manifest));
            candidates.Add((ScriptWorkflowWorkspaceLayout.WorkflowLedgerSchemaFile, ScriptWorkflowWorkspaceConfigKind.LedgerSchema));
        }

        var configs = new List<ScriptWorkflowWorkspaceConfigFile>();
        foreach (var (fileName, kind) in candidates)
        {
            var path = ScriptWorkflowWorkspaceLayout.Combine(basePath, fileName);
            var entry = await GetOptionalFile(workspace, path, cancellationToken).ConfigureAwait(false);
            if (!entry.Success)
            {
                return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceConfigFile>>.Failed(entry.Error!);
            }

            if (entry.Value != null)
            {
                configs.Add(new ScriptWorkflowWorkspaceConfigFile
                {
                    Path = entry.Value.Path,
                    Name = entry.Value.Name,
                    Kind = kind,
                    Scope = scope,
                    WorkflowName = workflowName,
                    Length = entry.Value.Length,
                    LastModifiedAt = entry.Value.LastModifiedAt
                });
            }
        }

        return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkflowWorkspaceConfigFile>>.Succeeded(
            new ReadOnlyCollection<ScriptWorkflowWorkspaceConfigFile>(configs));
    }

    private static async Task<ManifestRead> ReadManifest(
        IScriptWorkspace workspace,
        string workflowName,
        CancellationToken cancellationToken)
    {
        var path = ScriptWorkflowWorkspaceLayout.WorkflowFilePath(
            workflowName,
            ScriptWorkflowWorkspaceLayout.WorkflowManifestFile);
        var manifest = await workspace.ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (!manifest.Success)
        {
            return manifest.Error?.Code == ScriptWorkspaceErrorCode.NotFound
                ? new ManifestRead(true, null)
                : new ManifestRead(true, manifest.Error?.Message);
        }

        try
        {
            var model = JsonSerializer.Deserialize<WorkflowManifest>(
                manifest.Value!.Content.Span,
                JsonOptions);
            return new ManifestRead(model?.Enabled ?? true, null);
        }
        catch (JsonException ex)
        {
            return new ManifestRead(true, ex.Message);
        }
    }

    private static async Task<ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>> ListExistingDirectory(
        IScriptWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await workspace.ListAsync(path, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            return ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Succeeded(result.Value!);
        }

        return result.Error?.Code == ScriptWorkspaceErrorCode.NotFound
            ? ScriptWorkflowWorkspaceResult<IReadOnlyList<ScriptWorkspaceEntry>>.Succeeded(Array.Empty<ScriptWorkspaceEntry>())
            : Failed<IReadOnlyList<ScriptWorkspaceEntry>>(result.Error!);
    }

    private static async Task<ScriptWorkflowWorkspaceResult<ScriptWorkspaceEntry?>> GetOptionalFile(
        IScriptWorkspace workspace,
        string path,
        CancellationToken cancellationToken)
    {
        var result = await workspace.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            return result.Value!.Kind == ScriptWorkspaceEntryKind.File
                ? ScriptWorkflowWorkspaceResult<ScriptWorkspaceEntry?>.Succeeded(result.Value)
                : ScriptWorkflowWorkspaceResult<ScriptWorkspaceEntry?>.Succeeded(null);
        }

        return result.Error?.Code == ScriptWorkspaceErrorCode.NotFound
            ? ScriptWorkflowWorkspaceResult<ScriptWorkspaceEntry?>.Succeeded(null)
            : Failed<ScriptWorkspaceEntry?>(result.Error!);
    }

    private static ScriptWorkflowWorkspaceFile ToWorkspaceFile(
        ScriptWorkspaceEntry entry,
        ScriptWorkflowWorkspaceScope scope,
        string? workflowName) =>
        new()
        {
            Path = entry.Path,
            Name = entry.Name,
            Scope = scope,
            WorkflowName = workflowName,
            Length = entry.Length,
            LastModifiedAt = entry.LastModifiedAt
        };

    private static bool HasConfig(
        IEnumerable<ScriptWorkflowWorkspaceConfigFile> configs,
        ScriptWorkflowWorkspaceConfigKind kind) =>
        configs.Any(config => config.Kind == kind);

    private static IReadOnlySet<string> NormalizeExtensions(IEnumerable<string>? extensions)
    {
        if (extensions == null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var normalized = extensions
            .Select(extension => extension.Trim())
            .Where(extension => extension.Length > 0)
            .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return normalized;
    }

    private static string GetExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : name[dot..];
    }

    private static ScriptWorkflowWorkspaceResult<T> Failed<T>(
        ScriptWorkspaceError error) =>
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

    private sealed record WorkflowManifest(bool Enabled = true);

    private sealed record ManifestRead(bool Enabled, string? Error);
}
