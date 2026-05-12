using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkflowWorkspaceDiscoveryTests
{
    [Fact]
    public void Discovery_can_be_registered_and_retrieved_from_host_context()
    {
        var discovery = new ScriptWorkflowWorkspaceDiscovery();
        var context = ScriptHostContext.Empty.WithCapability<IScriptWorkflowWorkspaceDiscovery>(discovery);

        Assert.True(context.TryGetCapability<IScriptWorkflowWorkspaceDiscovery>(out var found));
        Assert.Same(discovery, found);
        Assert.Same(discovery, context.GetCapability<IScriptWorkflowWorkspaceDiscovery>());
    }

    [Fact]
    public async Task Discovery_reports_global_and_workflow_layout()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "scripts", "nested"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "lib"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "views"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "alpha", "scripts", "nested"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "alpha", "lib"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "alpha", "views"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "beta", "views"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts", "root.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts", "nested", "job.lua"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts", "notes.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "lib", "util.janet"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "views", "review.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "views", "queue-view.schema.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "events.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "queues.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "tools.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "scripts", "fetch.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "scripts", "nested", "parse.janet"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "lib", "helpers.lua"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "views", "queue.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "views", "queue.schema.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "events.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "queues.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "tools.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "workflow.json"), """{"enabled":false}""");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "workflow-ledger.schema.json"), """{"version":1}""");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "beta", "views", "schema-only.schema.json"), "{}");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var result = await discovery.DiscoverAsync(workspace);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(["scripts/nested/job.lua", "scripts/root.js"], result.Value!.GlobalScripts.Select(file => file.Path).ToArray());
        Assert.Equal(["lib/util.janet"], result.Value.GlobalLibraries.Select(file => file.Path).ToArray());
        Assert.Equal(["views/review.json"], result.Value.GlobalViews.Select(file => file.Path).ToArray());
        Assert.Equal(
            [ScriptWorkflowWorkspaceConfigKind.Events, ScriptWorkflowWorkspaceConfigKind.Queues, ScriptWorkflowWorkspaceConfigKind.Tools],
            result.Value.GlobalConfigFiles.Select(file => file.Kind).ToArray());

        Assert.Equal(["alpha", "beta"], result.Value.Workflows.Select(workflow => workflow.Name).ToArray());
        var alpha = result.Value.Workflows[0];
        Assert.False(alpha.Enabled);
        Assert.True(alpha.HasEvents);
        Assert.True(alpha.HasWorkers);
        Assert.True(alpha.HasTools);
        Assert.True(alpha.HasViews);
        Assert.True(alpha.HasLedgerSchema);
        Assert.Equal(2, alpha.ScriptCount);
        Assert.Equal(["workflows/alpha/scripts/fetch.js", "workflows/alpha/scripts/nested/parse.janet"], alpha.Scripts.Select(file => file.Path).ToArray());
        Assert.Equal(["workflows/alpha/lib/helpers.lua"], alpha.Libraries.Select(file => file.Path).ToArray());
        Assert.Equal(["workflows/alpha/views/queue.json"], alpha.Views.Select(file => file.Path).ToArray());
        Assert.Equal(
            [ScriptWorkflowWorkspaceConfigKind.Events, ScriptWorkflowWorkspaceConfigKind.Queues, ScriptWorkflowWorkspaceConfigKind.Tools, ScriptWorkflowWorkspaceConfigKind.Manifest, ScriptWorkflowWorkspaceConfigKind.LedgerSchema],
            alpha.ConfigFiles.Select(file => file.Kind).ToArray());

        var beta = result.Value.Workflows[1];
        Assert.True(beta.Enabled);
        Assert.False(beta.HasViews);
        Assert.Equal(0, beta.ScriptCount);
    }

    [Fact]
    public async Task Snapshot_overlay_combines_global_and_active_workflow_tiers()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "scripts"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "lib"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "views"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "scripts"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "lib"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "views"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts", "global.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "lib", "global-lib.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "views", "review.json"), """{"source":"global"}""");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "views", "global-only.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "events.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "scripts", "workflow.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "lib", "workflow-lib.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "views", "review.json"), """{"source":"workflow"}""");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "queues.json"), "{}");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var snapshot = (await discovery.DiscoverAsync(workspace)).Value!;
        var overlay = snapshot.CreateOverlay("crawl");
        var globalOnly = snapshot.CreateOverlay(null);

        Assert.Equal("crawl", overlay.WorkflowName);
        Assert.Equal("workflows/crawl", overlay.WorkflowPath);
        Assert.Equal(["events.json", "workflows/crawl/queues.json"], overlay.EffectiveConfigFiles.Select(file => file.Path).ToArray());
        Assert.Equal(["views/global-only.json", "workflows/crawl/views/review.json"], overlay.EffectiveViews.Select(file => file.Path).ToArray());
        Assert.Equal(["scripts/global.js"], overlay.GlobalScripts.Select(file => file.Path).ToArray());
        Assert.Equal(["workflows/crawl/scripts/workflow.js"], overlay.WorkflowScripts.Select(file => file.Path).ToArray());
        Assert.Equal(["lib/global-lib.js"], overlay.GlobalLibraries.Select(file => file.Path).ToArray());
        Assert.Equal(["workflows/crawl/lib/workflow-lib.js"], overlay.WorkflowLibraries.Select(file => file.Path).ToArray());
        Assert.Null(globalOnly.WorkflowName);
        Assert.Equal(["views/global-only.json", "views/review.json"], globalOnly.EffectiveViews.Select(file => file.Path).ToArray());
    }

    [Fact]
    public async Task Discovery_treats_missing_standard_folders_as_empty()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var result = await discovery.DiscoverAsync(workspace);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Value!.GlobalScripts);
        Assert.Empty(result.Value.GlobalLibraries);
        Assert.Empty(result.Value.GlobalViews);
        Assert.Empty(result.Value.GlobalConfigFiles);
        Assert.Empty(result.Value.Workflows);
    }

    [Fact]
    public async Task Discovery_records_invalid_manifest_but_keeps_workflow_enabled()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "broken"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "broken", "workflow.json"), "{ no");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var result = await discovery.DiscoverAsync(workspace);

        Assert.True(result.Success, result.Error?.Message);
        var workflow = Assert.Single(result.Value!.Workflows);
        Assert.Equal("broken", workflow.Name);
        Assert.True(workflow.Enabled);
        Assert.NotNull(workflow.ManifestError);
    }

    [Fact]
    public async Task Load_ledger_schemas_reads_schema_files_and_reports_invalid_schemas_through_validator()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "valid"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "invalid"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "workflows", "valid", "workflow-ledger.schema.json"),
            """
            {
              "version": 1,
              "stages": ["fetch"],
              "states": ["pending", "running"],
              "transitions": [
                { "fromStage": "fetch", "fromState": "pending", "toStage": "fetch", "toState": "running" }
              ]
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "workflows", "invalid", "workflow-ledger.schema.json"),
            """{"version":2}""");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var result = await discovery.LoadLedgerSchemasAsync(workspace);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(["invalid", "valid"], result.Value!.SchemaJsonByWorkflow.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.True(result.Value.Validator.TryGetSchema("valid", out var schema));
        Assert.Single(schema.Transitions);
        Assert.True(result.Value.Validator.TryGetInvalidSchemaError("invalid", out var error));
        Assert.Contains("version must be 1", error);
    }

    [Fact]
    public async Task Discovery_allows_hosts_to_extend_script_extensions()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts", "typed.ts"), "");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var defaults = await discovery.DiscoverAsync(workspace);
        var extended = await discovery.DiscoverAsync(
            workspace,
            new ScriptWorkflowWorkspaceDiscoveryOptions { ScriptExtensions = [".js", ".lua", ".janet", ".ts"] });

        Assert.True(defaults.Success, defaults.Error?.Message);
        Assert.Empty(defaults.Value!.GlobalScripts);
        Assert.True(extended.Success, extended.Error?.Message);
        Assert.Equal("scripts/typed.ts", Assert.Single(extended.Value!.GlobalScripts).Path);
    }

    [Fact]
    public void Config_relative_resolution_matches_global_and_workflow_base_paths()
    {
        var global = ScriptWorkflowWorkspaceResolution.ResolveConfigRelativeScriptPath(null, "scripts/root.js");
        var workflow = ScriptWorkflowWorkspaceResolution.ResolveConfigRelativeScriptPath("crawl", "scripts/fetch.js");
        var sibling = ScriptWorkflowWorkspaceResolution.ResolveConfigRelativeScriptPath("crawl", "views/../scripts/fetch.js");
        var rootFallback = ScriptWorkflowWorkspaceResolution.ResolveConfigRelativeScriptPath("crawl", "../../scripts/root.js");
        var escape = ScriptWorkflowWorkspaceResolution.ResolveConfigRelativeScriptPath("crawl", "../../../outside.js");

        Assert.True(global.Success, global.Error?.Message);
        Assert.Equal("scripts/root.js", global.Value);
        Assert.True(workflow.Success, workflow.Error?.Message);
        Assert.Equal("workflows/crawl/scripts/fetch.js", workflow.Value);
        Assert.True(sibling.Success, sibling.Error?.Message);
        Assert.Equal("workflows/crawl/scripts/fetch.js", sibling.Value);
        Assert.True(rootFallback.Success, rootFallback.Error?.Message);
        Assert.Equal("scripts/root.js", rootFallback.Value);
        Assert.False(escape.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, escape.Error?.Code);
    }

    [Fact]
    public void Lua_module_probe_paths_match_workflow_then_global_order()
    {
        var workflow = ScriptWorkflowWorkspaceResolution.GetLuaModuleProbePaths("helpers", "crawl");
        var global = ScriptWorkflowWorkspaceResolution.GetLuaModuleProbePaths("helpers.lua");

        Assert.True(workflow.Success, workflow.Error?.Message);
        Assert.Equal(
            [
                "workflows/crawl/scripts/helpers.lua",
                "workflows/crawl/lib/helpers.lua",
                "lib/helpers.lua",
                "scripts/helpers.lua"
            ],
            workflow.Value);
        Assert.True(global.Success, global.Error?.Message);
        Assert.Equal(["lib/helpers.lua", "scripts/helpers.lua"], global.Value);
    }

    [Fact]
    public void JavaScript_module_probe_paths_match_importer_directory_then_workspace_root()
    {
        var workflowRelative = ScriptWorkflowWorkspaceResolution.GetJavaScriptModuleProbePaths(
            "workflows/crawl/scripts/main.js",
            "../lib/utils.js");
        var workflowRootFallback = ScriptWorkflowWorkspaceResolution.GetJavaScriptModuleProbePaths(
            "workflows/crawl/scripts/main.js",
            "lib/shared.js");
        var globalRelative = ScriptWorkflowWorkspaceResolution.GetJavaScriptModuleProbePaths(
            "scripts/main.js",
            "./helpers.js");
        var escape = ScriptWorkflowWorkspaceResolution.GetJavaScriptModuleProbePaths(
            "scripts/main.js",
            "../../outside.js");

        Assert.True(workflowRelative.Success, workflowRelative.Error?.Message);
        Assert.Equal(["workflows/crawl/lib/utils.js"], workflowRelative.Value);
        Assert.True(workflowRootFallback.Success, workflowRootFallback.Error?.Message);
        Assert.Equal(["workflows/crawl/scripts/lib/shared.js", "lib/shared.js"], workflowRootFallback.Value);
        Assert.True(globalRelative.Success, globalRelative.Error?.Message);
        Assert.Equal(["scripts/helpers.js", "helpers.js"], globalRelative.Value);
        Assert.False(escape.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, escape.Error?.Code);
    }

    [Fact]
    public async Task Discovery_returns_workspace_error_when_standard_folder_is_not_a_directory()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "scripts"), "");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();

        var result = await discovery.DiscoverAsync(workspace);

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.WorkspaceError, result.Error?.Code);
        Assert.Equal(ScriptWorkspaceErrorCode.NotADirectory, result.Error?.WorkspaceError?.Code);
    }

    [Fact]
    public async Task Discovery_observes_cancellation_tokens()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var discovery = new ScriptWorkflowWorkspaceDiscovery();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            discovery.DiscoverAsync(workspace, cancellationToken: cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            discovery.LoadLedgerSchemasAsync(workspace, cancellationToken: cts.Token));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"guida-scripting-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
