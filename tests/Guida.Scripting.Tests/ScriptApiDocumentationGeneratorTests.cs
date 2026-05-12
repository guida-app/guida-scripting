namespace Guida.Scripting.Tests;

public sealed class ScriptApiDocumentationGeneratorTests
{
    [Fact]
    public void Generate_markdown_is_deterministic_and_documents_public_surfaces()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var first = ScriptApiDocumentationGenerator.GenerateMarkdown(registry);
        var second = ScriptApiDocumentationGenerator.GenerateMarkdown(registry);

        Assert.Equal(first, second);
        Assert.StartsWith("# Guida Scripting API", first);
        Assert.Contains("Generated from public `ScriptApiRegistry` metadata.", first);
        Assert.Contains("## Data Interfaces", first);
        Assert.Contains("## API Groups", first);
        Assert.Contains("### `g.store`", first);
        Assert.Contains("### `g.queue`", first);
        Assert.Contains("### `g.worker`", first);
        Assert.Contains("### `g.workers`", first);
        Assert.Contains("### `g.workflow`", first);
        Assert.Contains("### `g.workflows`", first);
        Assert.Contains("### `g.workspace`", first);
        Assert.Contains("`g.queue.registerStrategy(name: string, fnOrPath:", first);
        Assert.Contains("`g.workflow.items.claimNext(filter?: WorkflowLedgerItemQuery, leaseOptions?: WorkflowLedgerClaimOptions)`", first);
    }

    [Fact]
    public void Generate_markdown_supports_options()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var output = ScriptApiDocumentationGenerator.GenerateMarkdown(
            registry,
            new ScriptApiDocumentationOptions
            {
                Title = "Public API",
                IncludeInterfaces = false,
                IncludeTypeAliases = false
            });

        Assert.StartsWith("# Public API", output);
        Assert.DoesNotContain("## Data Interfaces", output);
        Assert.Contains("## API Groups", output);
    }

    [Fact]
    public void Generate_markdown_excludes_private_browser_namespace_entries()
    {
        var output = ScriptApiDocumentationGenerator.GenerateMarkdown(
            ScriptApiKnownRegistries.CreateExtractedCapabilities());

        Assert.DoesNotContain("### `g.dom`", output);
        Assert.DoesNotContain("### `g.tabs`", output);
        Assert.DoesNotContain("### `g.intercept`", output);
        Assert.DoesNotContain("### `g.screenshot`", output);
        Assert.DoesNotContain("### `g.page`", output);
        Assert.DoesNotContain("### `g.pane`", output);
        Assert.DoesNotContain("### `g.network`", output);
        Assert.DoesNotContain("### `g.layout`", output);
        Assert.DoesNotContain("### `g.clipboard`", output);
        Assert.DoesNotContain("`g.dom.", output);
        Assert.DoesNotContain("`g.tabs.", output);
        Assert.DoesNotContain("`g.intercept.", output);
    }

    [Fact]
    public void Generate_markdown_throws_for_invalid_registry()
    {
        var registry = new ScriptApiRegistry
        {
            Groups =
            [
                new ScriptApiGroup { Name = "OneApi", PropertyName = "one" },
                new ScriptApiGroup { Name = "TwoApi", PropertyName = "one" }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => ScriptApiDocumentationGenerator.GenerateMarkdown(registry));
        Assert.Contains("Duplicate group property name 'one'.", exception.Message);
    }
}
