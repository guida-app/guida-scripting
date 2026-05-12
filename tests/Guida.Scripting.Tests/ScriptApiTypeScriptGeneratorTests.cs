namespace Guida.Scripting.Tests;

public sealed class ScriptApiTypeScriptGeneratorTests
{
    [Fact]
    public void Generate_is_deterministic_and_declares_global_g()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var first = ScriptApiTypeScriptGenerator.Generate(registry);
        var second = ScriptApiTypeScriptGenerator.Generate(registry);

        Assert.Equal(first, second);
        Assert.Contains("Guida Scripting API Type Definitions", first);
        Assert.Contains("Auto-generated from ScriptApiRegistry", first);
        Assert.DoesNotContain("Generated:", first);
        Assert.Contains("interface Guida {", first);
        Assert.Contains("declare const g: Guida;", first);
    }

    [Fact]
    public void Generate_supports_type_aliases_interfaces_api_groups_and_top_level_functions()
    {
        var registry = new ScriptApiRegistry
        {
            TypeAliases =
            [
                new ScriptApiTypeAlias("Mode", "\"read\" | \"write\"", "Mode alias")
            ],
            Interfaces =
            [
                new ScriptApiInterface
                {
                    Name = "Thing",
                    Description = "Thing model",
                    Properties =
                    [
                        new ScriptApiProperty("id", ScriptApiType.String, description: "Thing ID")
                    ]
                }
            ],
            Functions =
            [
                new ScriptApiFunction
                {
                    Name = "ping",
                    FullName = "g.ping",
                    Description = "Ping the host.",
                    ReturnType = ScriptApiType.String
                }
            ],
            Groups =
            [
                new ScriptApiGroup
                {
                    Name = "ThingsApi",
                    PropertyName = "things",
                    Description = "Thing operations",
                    Functions =
                    [
                        new ScriptApiFunction
                        {
                            Name = "get",
                            FullName = "g.things.get",
                            Namespace = "things",
                            Description = "Get one thing.",
                            Parameters =
                            [
                                new ScriptApiParameter("id", ScriptApiType.String)
                            ],
                            ReturnType = ScriptApiType.Custom("Thing | null")
                        }
                    ]
                }
            ]
        };

        var output = ScriptApiTypeScriptGenerator.Generate(registry);

        Assert.Contains("type Mode = \"read\" | \"write\";", output);
        Assert.Contains("interface Thing {", output);
        Assert.Contains("  id: string;", output);
        Assert.Contains("interface ThingsApi {", output);
        Assert.Contains("  get(id: string): Thing | null;", output);
        Assert.Contains("  ping(): string;", output);
        Assert.Contains("  things: ThingsApi;", output);
    }

    [Fact]
    public void Generate_can_omit_descriptions_and_customize_global_names()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var output = ScriptApiTypeScriptGenerator.Generate(
            registry,
            new ScriptApiTypeScriptGeneratorOptions
            {
                IncludeDescriptions = false,
                IncludeHeader = false,
                RootInterfaceName = "ScriptHost",
                GlobalVariableName = "host"
            });

        Assert.StartsWith("// Interfaces", output);
        Assert.DoesNotContain("/**", output);
        Assert.Contains("interface ScriptHost {", output);
        Assert.Contains("declare const host: ScriptHost;", output);
    }

    [Fact]
    public void Generate_throws_for_invalid_registry()
    {
        var registry = new ScriptApiRegistry
        {
            Groups =
            [
                new ScriptApiGroup { Name = "OneApi", PropertyName = "one" },
                new ScriptApiGroup { Name = "TwoApi", PropertyName = "one" }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => ScriptApiTypeScriptGenerator.Generate(registry));
        Assert.Contains("Duplicate group property name 'one'.", exception.Message);
    }

    [Fact]
    public void Generate_contains_store_queue_worker_workflow_workflows_and_workspace_declarations()
    {
        var output = GenerateExtractedCapabilities();

        Assert.Contains("interface StoreApi {", output);
        Assert.Contains("  put(collection: string, key: string, data: any): void;", output);
        Assert.Contains("  get(collection: string, key: string): StoreDoc | null;", output);
        Assert.Contains("  store: StoreApi;", output);

        Assert.Contains("interface QueueApi {", output);
        Assert.Contains("  waitForItem(name: string, options?: WaitForItemOptions): Promise<QueueItem | null>;", output);
        Assert.Contains("  registerStrategy(name: string, fnOrPath: ((groups: Record<string, number>, ctx: { lastGroup: string | null, callCount: number, state: Record<string, any> }) => string | null) | string): void;", output);
        Assert.Contains("  queue: QueueApi;", output);

        Assert.Contains("interface WorkersApi {", output);
        Assert.Contains("  status(): WorkerPoolStatus[];", output);
        Assert.Contains("  workers: WorkersApi;", output);

        Assert.Contains("interface WorkerApi {", output);
        Assert.Contains("  workflow: WorkerWorkflowApi;", output);
        Assert.Contains("  getContext(): WorkerContext;", output);
        Assert.Contains("  worker: WorkerApi;", output);

        Assert.Contains("interface WorkflowLedgerApi {", output);
        Assert.Contains("  runs: WorkflowLedgerRunsApi;", output);
        Assert.Contains("  items: WorkflowLedgerItemsApi;", output);
        Assert.Contains("  workflow: WorkflowLedgerApi;", output);

        Assert.Contains("interface WorkflowsApi {", output);
        Assert.Contains("  getActive(): WorkflowInfo | null;", output);
        Assert.Contains("  switch(name: string): void;", output);
        Assert.Contains("  workflows: WorkflowsApi;", output);

        Assert.Contains("interface WorkspaceApi {", output);
        Assert.Contains("  readFile(path: string): WorkspaceFileContent;", output);
        Assert.Contains("  writeFile(path: string, content: string, options?: WorkspaceWriteOptions): void;", output);
        Assert.Contains("  workspace: WorkspaceApi;", output);
    }

    [Fact]
    public void Generate_contains_nested_worker_and_workflow_api_interfaces()
    {
        var output = GenerateExtractedCapabilities();

        Assert.Contains("interface WorkerWorkflowApi {", output);
        Assert.Contains("  complete(options?: WorkflowLedgerLeaseOptions): WorkflowLedgerItem;", output);
        Assert.Contains("  fail(errorOrOptions: string | WorkflowLedgerFailureOptions): WorkflowLedgerItem;", output);

        Assert.Contains("interface WorkflowLedgerRunsApi {", output);
        Assert.Contains("  start(workflowName: string, options?: WorkflowLedgerRunOptions): WorkflowLedgerRun;", output);
        Assert.Contains("  fail(runId: string, errorOrOptions: string | WorkflowLedgerRunFailOptions): WorkflowLedgerRun;", output);

        Assert.Contains("interface WorkflowLedgerItemsApi {", output);
        Assert.Contains("  upsert(input: WorkflowLedgerItemUpsertInput): WorkflowLedgerItem;", output);
        Assert.Contains("  enqueue(input: WorkflowLedgerQueueEnqueueInput): WorkflowLedgerQueueEnqueueResult;", output);
        Assert.Contains("  claimNext(filter?: WorkflowLedgerItemQuery, leaseOptions?: WorkflowLedgerClaimOptions): WorkflowLedgerItem[];", output);
    }

    [Fact]
    public void Generate_excludes_private_browser_namespace_declarations()
    {
        var output = GenerateExtractedCapabilities();

        Assert.DoesNotContain("interface DomApi", output);
        Assert.DoesNotContain("interface TabsApi", output);
        Assert.DoesNotContain("interface InterceptApi", output);
        Assert.DoesNotContain("interface ScreenshotApi", output);
        Assert.DoesNotContain("interface PageApi", output);
        Assert.DoesNotContain("interface PaneApi", output);
        Assert.DoesNotContain("interface NetworkApi", output);
        Assert.DoesNotContain("interface LayoutApi", output);
        Assert.DoesNotContain("interface ClipboardApi", output);

        Assert.DoesNotContain("  dom:", output);
        Assert.DoesNotContain("  tabs:", output);
        Assert.DoesNotContain("  intercept:", output);
        Assert.DoesNotContain("  screenshot:", output);
        Assert.DoesNotContain("  page:", output);
        Assert.DoesNotContain("  pane:", output);
        Assert.DoesNotContain("  network:", output);
        Assert.DoesNotContain("  layout:", output);
        Assert.DoesNotContain("  clipboard:", output);
    }

    private static string GenerateExtractedCapabilities() =>
        ScriptApiTypeScriptGenerator.Generate(ScriptApiKnownRegistries.CreateExtractedCapabilities());
}
