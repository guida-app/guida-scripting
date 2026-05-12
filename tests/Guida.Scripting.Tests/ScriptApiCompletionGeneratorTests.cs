namespace Guida.Scripting.Tests;

public sealed class ScriptApiCompletionGeneratorTests
{
    [Fact]
    public void Generate_completions_returns_sorted_function_metadata()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var completions = ScriptApiCompletionGenerator.GenerateCompletions(registry);

        Assert.Equal(
            completions.Select(completion => completion.FullName).Order(StringComparer.Ordinal),
            completions.Select(completion => completion.FullName));
        Assert.Contains(completions, completion =>
            completion.FullName == "g.queue.waitForItem" &&
            completion.Signature == "await g.queue.waitForItem(name, [options])" &&
            completion.ReturnType == "Promise<QueueItem | null>" &&
            completion.IsAsync);
        Assert.Contains(completions, completion =>
            completion.FullName == "g.workflow.items.complete" &&
            completion.Signature == "g.workflow.items.complete(itemRef, [options])" &&
            completion.ReturnType == "WorkflowLedgerItem");
    }

    [Fact]
    public void Generate_completions_includes_parameter_and_return_details()
    {
        var completion = ScriptApiCompletionGenerator
            .GenerateCompletions(ScriptApiKnownRegistries.CreateExtractedCapabilities())
            .Single(completion => completion.FullName == "g.queue.registerStrategy");

        Assert.Contains("Register a custom dequeue strategy.", completion.Description);
        Assert.Contains("Parameters:", completion.Description);
        Assert.Contains("name: Strategy name", completion.Description);
        Assert.Contains("fnOrPath: Strategy function or workspace script file path", completion.Description);
    }

    [Fact]
    public void Generate_hover_docs_includes_global_and_group_docs()
    {
        var hoverDocs = ScriptApiCompletionGenerator.GenerateHoverDocs(
            ScriptApiKnownRegistries.CreateExtractedCapabilities());

        Assert.Equal("Global scripting API namespace.", hoverDocs["g"]);
        Assert.Equal("Workspace-scoped persistent document storage API", hoverDocs["g.store"]);
        Assert.Equal("Workspace-scoped durable workflow ledger API", hoverDocs["g.workflow"]);
        Assert.Contains("Mark a workflow item completed.", hoverDocs["g.workflow.items.complete"]);
    }

    [Fact]
    public void Build_function_lookup_indexes_all_functions_by_full_name()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var lookup = ScriptApiCompletionGenerator.BuildFunctionLookup(registry);

        Assert.Same(registry.FindFunction("g.store.get"), lookup["g.store.get"]);
        Assert.Same(registry.FindFunction("g.worker.workflow.fail"), lookup["g.worker.workflow.fail"]);
        Assert.Same(registry.FindFunction("g.workflow.runs.start"), lookup["g.workflow.runs.start"]);
    }

    [Fact]
    public void Generated_completion_metadata_excludes_private_browser_namespaces()
    {
        var completions = ScriptApiCompletionGenerator.GenerateCompletions(
            ScriptApiKnownRegistries.CreateExtractedCapabilities());
        var names = completions.Select(completion => completion.FullName).ToArray();

        Assert.DoesNotContain(names, name => name.StartsWith("g.dom.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.tabs.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.intercept.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.screenshot.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.page.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.pane.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.network.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.layout.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.StartsWith("g.clipboard.", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_completions_throws_for_invalid_registry()
    {
        var registry = new ScriptApiRegistry
        {
            Groups =
            [
                new ScriptApiGroup { Name = "OneApi", PropertyName = "one" },
                new ScriptApiGroup { Name = "TwoApi", PropertyName = "one" }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => ScriptApiCompletionGenerator.GenerateCompletions(registry));
        Assert.Contains("Duplicate group property name 'one'.", exception.Message);
    }
}
