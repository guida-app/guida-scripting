namespace Guida.Scripting.Tests;

public sealed class ScriptApiRegistryTests
{
    [Fact]
    public void Api_type_formats_typescript_compatible_type_strings()
    {
        Assert.Equal("string", ScriptApiType.String.ToTypeString());
        Assert.Equal("Promise<QueueItem | null>", ScriptApiType.Promise(ScriptApiType.Custom("QueueItem | null")).ToTypeString());
        Assert.Equal("string[]", ScriptApiType.ArrayOf(ScriptApiType.String).ToTypeString());
        Assert.Equal("number | null", ScriptApiType.Nullable(ScriptApiType.Number).ToTypeString());
        Assert.Equal(
            "Record<string, any>",
            ScriptApiType.Record(ScriptApiType.String, ScriptApiType.Any).ToTypeString());
    }

    [Fact]
    public void Extracted_capabilities_registry_is_structurally_valid()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        Assert.Empty(registry.Validate());
        Assert.NotEmpty(registry.Interfaces);
        Assert.NotEmpty(registry.Groups);
        Assert.NotEmpty(registry.GetAllFunctions());
    }

    [Fact]
    public void Extracted_capabilities_registry_contains_public_safe_groups_only()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();
        var groupNames = registry.Groups.Select(group => group.PropertyName).Order().ToArray();

        Assert.Equal(
            ["queue", "store", "worker", "workers", "workflow", "workflows", "workspace"],
            groupNames);

        Assert.DoesNotContain(registry.Groups, group => group.PropertyName is
            "dom" or
            "tabs" or
            "intercept" or
            "screenshot" or
            "page" or
            "pane" or
            "network" or
            "layout" or
            "clipboard");
    }

    [Fact]
    public void Store_registry_preserves_script_facing_method_names_and_shapes()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();
        var store = GetGroup(registry, "store");

        Assert.Equal(
            [
                "g.store.put",
                "g.store.get",
                "g.store.list",
                "g.store.search",
                "g.store.delete",
                "g.store.count",
                "g.store.clear",
                "g.store.collections"
            ],
            store.Functions.Select(function => function.FullName).ToArray());

        Assert.Equal(
            "  get(collection: string, key: string): StoreDoc | null;",
            registry.FindFunction("g.store.get")?.ToDeclarationString());
        Assert.Equal(
            "  list(collection: string, options?: StoreListOptions): StoreDoc[];",
            registry.FindFunction("g.store.list")?.ToDeclarationString());
    }

    [Fact]
    public void Queue_registry_preserves_strategy_capable_script_facing_shape()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();
        var queue = GetGroup(registry, "queue");

        Assert.Equal(
            [
                "g.queue.enqueue",
                "g.queue.dequeue",
                "g.queue.commit",
                "g.queue.abort",
                "g.queue.peek",
                "g.queue.count",
                "g.queue.clear",
                "g.queue.list",
                "g.queue.queues",
                "g.queue.deadLetter",
                "g.queue.retry",
                "g.queue.waitForItem",
                "g.queue.registerStrategy"
            ],
            queue.Functions.Select(function => function.FullName).ToArray());

        var waitForItem = registry.FindFunction("g.queue.waitForItem");
        Assert.NotNull(waitForItem);
        Assert.True(waitForItem.IsAsync);
        Assert.Equal("await g.queue.waitForItem(name, [options])", waitForItem.ToSignatureString());
        Assert.Equal(
            "  registerStrategy(name: string, fnOrPath: ((groups: Record<string, number>, ctx: { lastGroup: string | null, callCount: number, state: Record<string, any> }) => string | null) | string): void;",
            registry.FindFunction("g.queue.registerStrategy")?.ToDeclarationString());
    }

    [Fact]
    public void Worker_registry_keeps_pool_and_per_item_workflow_surfaces_distinct()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        Assert.Equal(
            ["g.workers.start", "g.workers.stop", "g.workers.pause", "g.workers.resume", "g.workers.status"],
            GetGroup(registry, "workers").Functions.Select(function => function.FullName).ToArray());

        var worker = GetGroup(registry, "worker");
        Assert.Equal("WorkerApi", worker.Name);
        Assert.Equal("workflow", Assert.Single(worker.Properties).Name);
        Assert.Equal("WorkerWorkflowApi", Assert.Single(worker.Properties).Type.ToTypeString());

        Assert.Equal(
            "  complete(options?: WorkflowLedgerLeaseOptions): WorkflowLedgerItem;",
            registry.FindFunction("g.worker.workflow.complete")?.ToDeclarationString());
        Assert.Equal(
            "  fail(errorOrOptions: string | WorkflowLedgerFailureOptions): WorkflowLedgerItem;",
            registry.FindFunction("g.worker.workflow.fail")?.ToDeclarationString());
    }

    [Fact]
    public void Workflow_registry_preserves_nested_ledger_and_workspace_management_shapes()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();
        var workflow = GetGroup(registry, "workflow");
        var workflowProperties = workflow.Properties.ToDictionary(property => property.Name);

        Assert.Equal("WorkflowLedgerApi", workflow.Name);
        Assert.Equal("WorkflowLedgerRunsApi", workflowProperties["runs"].Type.ToTypeString());
        Assert.Equal("WorkflowLedgerItemsApi", workflowProperties["items"].Type.ToTypeString());

        Assert.Equal(
            "  start(workflowName: string, options?: WorkflowLedgerRunOptions): WorkflowLedgerRun;",
            registry.FindFunction("g.workflow.runs.start")?.ToDeclarationString());
        Assert.Equal(
            "  upsert(input: WorkflowLedgerItemUpsertInput): WorkflowLedgerItem;",
            registry.FindFunction("g.workflow.items.upsert")?.ToDeclarationString());
        Assert.Equal(
            "  enqueue(input: WorkflowLedgerQueueEnqueueInput): WorkflowLedgerQueueEnqueueResult;",
            registry.FindFunction("g.workflow.items.enqueue")?.ToDeclarationString());
        Assert.Equal(
            "  claimNext(filter?: WorkflowLedgerItemQuery, leaseOptions?: WorkflowLedgerClaimOptions): WorkflowLedgerItem[];",
            registry.FindFunction("g.workflow.items.claimNext")?.ToDeclarationString());

        Assert.Equal(
            ["g.workflows.getActive", "g.workflows.list", "g.workflows.switch"],
            GetGroup(registry, "workflows").Functions.Select(function => function.FullName).ToArray());
    }

    [Fact]
    public void Workspace_registry_models_public_workspace_capability_without_browser_coupling()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();
        var workspace = GetGroup(registry, "workspace");

        Assert.Equal(
            ["g.workspace.getEntry", "g.workspace.list", "g.workspace.readFile", "g.workspace.writeFile"],
            workspace.Functions.Select(function => function.FullName).ToArray());
        Assert.Equal(
            "  writeFile(path: string, content: string, options?: WorkspaceWriteOptions): void;",
            registry.FindFunction("g.workspace.writeFile")?.ToDeclarationString());

        var entry = registry.Interfaces.Single(type => type.Name == "WorkspaceEntry");
        Assert.Contains(entry.Properties, property => property.Name == "kind" && property.Type.ToTypeString() == "\"file\" | \"directory\"");
    }

    private static ScriptApiGroup GetGroup(ScriptApiRegistry registry, string propertyName) =>
        registry.Groups.Single(group => group.PropertyName == propertyName);
}
