using System.Text;
using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkflowWorkspaceManagerTests
{
    [Fact]
    public void Manager_can_be_registered_and_retrieved_from_host_context()
    {
        var manager = new ScriptWorkflowWorkspaceManager();
        var context = ScriptHostContext.Empty.WithCapability<IScriptWorkflowWorkspaceManager>(manager);

        Assert.True(context.TryGetCapability<IScriptWorkflowWorkspaceManager>(out var found));
        Assert.Same(manager, found);
        Assert.Same(manager, context.GetCapability<IScriptWorkflowWorkspaceManager>());
    }

    [Fact]
    public async Task List_summarize_and_activate_track_active_workflow()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "alpha", "scripts"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "alpha", "views"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "beta"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "scripts", "run.js"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "views", "review.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "alpha", "events.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "beta", "workflow.json"), """{"enabled":false}""");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var manager = new ScriptWorkflowWorkspaceManager();
        var changed = new List<string?>();
        manager.ActiveWorkflowChanged += (_, activation) => changed.Add(activation.ActiveWorkflowName);

        var before = await manager.ListAsync(workspace);
        var disabled = await manager.ActivateAsync(workspace, "beta");
        var activated = await manager.ActivateAsync(workspace, "ALPHA");
        var after = await manager.ListAsync(workspace);
        var summary = await manager.SummarizeAsync(workspace);
        var deactivated = await manager.ActivateAsync(workspace, null);

        Assert.True(before.Success, before.Error?.Message);
        Assert.Null(before.Value!.ActiveWorkflowName);
        Assert.Equal(["alpha", "beta"], before.Value.Workflows.Select(workflow => workflow.Name).ToArray());
        Assert.False(disabled.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, disabled.Error?.Code);
        Assert.True(activated.Success, activated.Error?.Message);
        Assert.Equal("alpha", activated.Value!.ActiveWorkflowName);
        Assert.Equal("workflows/alpha", activated.Value.ActiveWorkflowPath);
        Assert.True(after.Success, after.Error?.Message);
        Assert.Equal("alpha", after.Value!.ActiveWorkflowName);
        Assert.True(after.Value.Workflows.Single(workflow => workflow.Name == "alpha").IsActive);
        Assert.True(summary.Success, summary.Error?.Message);
        Assert.Equal(2, summary.Value!.WorkflowCount);
        Assert.Equal(1, summary.Value.EnabledWorkflowCount);
        Assert.Equal(1, summary.Value.DisabledWorkflowCount);
        Assert.Equal(1, summary.Value.TotalScriptCount);
        Assert.Equal(1, summary.Value.TotalViewCount);
        Assert.True(deactivated.Success, deactivated.Error?.Message);
        Assert.Null(deactivated.Value!.ActiveWorkflowName);
        Assert.Equal(["alpha", null], changed);
    }

    [Fact]
    public async Task Inspect_returns_summary_overlay_and_all_workflow_files()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "views"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "scripts"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "notes"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "workflows", "crawl", "views"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "views", "queue.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "scripts", "main.lua"), "");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "notes", "readme.md"), "notes");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "views", "queue.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "workflows", "crawl", "tools.json"), "{}");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var manager = new ScriptWorkflowWorkspaceManager();

        var result = await manager.InspectAsync(workspace, "crawl");

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("crawl", result.Value!.Workflow.Name);
        Assert.Equal(1, result.Value.Summary.ScriptCount);
        Assert.Equal(1, result.Value.Summary.ViewCount);
        Assert.True(result.Value.Summary.HasTools);
        Assert.Equal(["workflows/crawl/views/queue.json"], result.Value.Overlay.EffectiveViews.Select(file => file.Path).ToArray());
        Assert.Equal(
            [
                "workflows/crawl/notes/readme.md",
                "workflows/crawl/scripts/main.lua",
                "workflows/crawl/tools.json",
                "workflows/crawl/views/queue.json"
            ],
            result.Value.Files.Select(file => file.Path).ToArray());
    }

    [Fact]
    public async Task Create_and_set_enabled_manage_manifest_state()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var manager = new ScriptWorkflowWorkspaceManager();

        var invalid = await manager.CreateAsync(workspace, "bad/name");
        var created = await manager.CreateAsync(
            workspace,
            "crawl",
            new ScriptWorkflowWorkspaceCreateOptions { Enabled = true, Summary = "Crawl queue" });
        var duplicate = await manager.CreateAsync(workspace, "crawl");
        var activated = await manager.ActivateAsync(workspace, "crawl");
        var disabled = await manager.SetEnabledAsync(workspace, "crawl", enabled: false);
        var read = await workspace.ReadFileAsync("workflows/crawl/workflow.json");

        Assert.False(invalid.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, invalid.Error?.Code);
        Assert.True(created.Success, created.Error?.Message);
        Assert.Equal("crawl", created.Value!.Name);
        Assert.True(created.Value.Enabled);
        Assert.False(duplicate.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.AlreadyExists, duplicate.Error?.Code);
        Assert.True(activated.Success, activated.Error?.Message);
        Assert.True(disabled.Success, disabled.Error?.Message);
        Assert.False(disabled.Value!.Enabled);
        Assert.Null(manager.ActiveWorkflowName);
        Assert.True(read.Success, read.Error?.Message);
        Assert.Contains("\"enabled\": false", Encoding.UTF8.GetString(read.Value!.Content.Span));
    }

    [Fact]
    public async Task Export_and_import_round_trip_workflow_files()
    {
        using var sourceTemp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(sourceTemp.Path, "workflows", "crawl", "scripts"));
        Directory.CreateDirectory(Path.Combine(sourceTemp.Path, "workflows", "crawl", "notes"));
        await File.WriteAllTextAsync(Path.Combine(sourceTemp.Path, "workflows", "crawl", "workflow.json"), """{"enabled":true}""");
        await File.WriteAllTextAsync(Path.Combine(sourceTemp.Path, "workflows", "crawl", "scripts", "main.js"), "export const x = 1;");
        await File.WriteAllTextAsync(Path.Combine(sourceTemp.Path, "workflows", "crawl", "notes", "readme.txt"), "hello");
        using var targetTemp = TemporaryDirectory.Create();
        var source = new ScriptFileSystemWorkspace(sourceTemp.Path);
        var target = new ScriptFileSystemWorkspace(targetTemp.Path);
        var manager = new ScriptWorkflowWorkspaceManager();

        var exported = await manager.ExportAsync(source, "crawl");
        var imported = await manager.ImportAsync(target, exported.Value!);
        var duplicate = await manager.ImportAsync(target, exported.Value!);
        var overwritten = await manager.ImportAsync(
            target,
            exported.Value!,
            new ScriptWorkflowWorkspaceImportOptions { OverwriteExisting = true });
        var script = await target.ReadFileAsync("workflows/crawl/scripts/main.js");
        var unsafeExport = exported.Value! with
        {
            WorkflowName = "unsafe",
            Files = [new ScriptWorkflowWorkspaceExportFile { Path = "../outside.js", Content = [1] }]
        };
        var unsafeImport = await manager.ImportAsync(target, unsafeExport);

        Assert.True(exported.Success, exported.Error?.Message);
        Assert.Equal(
            ["notes/readme.txt", "scripts/main.js", "workflow.json"],
            exported.Value!.Files.Select(file => file.Path).ToArray());
        Assert.True(imported.Success, imported.Error?.Message);
        Assert.Equal("crawl", imported.Value!.WorkflowName);
        Assert.Equal(3, imported.Value.ImportedFileCount);
        Assert.False(duplicate.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.AlreadyExists, duplicate.Error?.Code);
        Assert.True(overwritten.Success, overwritten.Error?.Message);
        Assert.True(script.Success, script.Error?.Message);
        Assert.Equal("export const x = 1;", Encoding.UTF8.GetString(script.Value!.Content.Span));
        Assert.False(unsafeImport.Success);
        Assert.Equal(ScriptWorkflowWorkspaceErrorCode.InvalidRequest, unsafeImport.Error?.Code);
    }

    [Fact]
    public async Task Public_methods_observe_cancellation_tokens()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);
        var manager = new ScriptWorkflowWorkspaceManager();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            manager.ListAsync(workspace, cancellationToken: cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            manager.ActivateAsync(workspace, null, cancellationToken: cts.Token));
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
