using System.Text.Json;

namespace Guida.Scripting.Tests;

public sealed class ScriptApiManifestGeneratorTests
{
    [Fact]
    public void Create_document_projects_registry_to_sorted_manifest()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var manifest = ScriptApiManifestGenerator.CreateDocument(registry);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("ScriptApiRegistry", manifest.GeneratedFrom);
        Assert.Equal(
            manifest.Groups.Select(group => group.PropertyName).Order(StringComparer.Ordinal),
            manifest.Groups.Select(group => group.PropertyName));
        Assert.Equal(
            manifest.Functions.Select(function => function.FullName).Order(StringComparer.Ordinal),
            manifest.Functions.Select(function => function.FullName));
        Assert.Contains(manifest.Groups, group => group.PropertyName == "workflow" && group.Name == "WorkflowLedgerApi");
        Assert.Contains(manifest.Functions, function => function.FullName == "g.workflow.items.claimNext");
    }

    [Fact]
    public void Generate_json_is_deterministic_and_parseable()
    {
        var registry = ScriptApiKnownRegistries.CreateExtractedCapabilities();

        var first = ScriptApiManifestGenerator.GenerateJson(registry);
        var second = ScriptApiManifestGenerator.GenerateJson(registry);

        Assert.Equal(first, second);
        Assert.EndsWith(Environment.NewLine, first);

        using var document = JsonDocument.Parse(first);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("ScriptApiRegistry", document.RootElement.GetProperty("generatedFrom").GetString());
        Assert.True(document.RootElement.GetProperty("groups").GetArrayLength() > 0);
        Assert.True(document.RootElement.GetProperty("functions").GetArrayLength() > 0);
    }

    [Fact]
    public void Generate_json_contains_public_extracted_surfaces()
    {
        var json = ScriptApiManifestGenerator.GenerateJson(ScriptApiKnownRegistries.CreateExtractedCapabilities());

        Assert.Contains("\"propertyName\": \"store\"", json);
        Assert.Contains("\"propertyName\": \"queue\"", json);
        Assert.Contains("\"propertyName\": \"worker\"", json);
        Assert.Contains("\"propertyName\": \"workers\"", json);
        Assert.Contains("\"propertyName\": \"workflow\"", json);
        Assert.Contains("\"propertyName\": \"workflows\"", json);
        Assert.Contains("\"propertyName\": \"workspace\"", json);
        Assert.Contains("\"fullName\": \"g.queue.registerStrategy\"", json);
        Assert.Contains("\"fullName\": \"g.worker.workflow.complete\"", json);
        Assert.Contains("\"fullName\": \"g.workflow.items.enqueue\"", json);
    }

    [Fact]
    public void Generate_json_excludes_private_browser_namespace_entries()
    {
        var json = ScriptApiManifestGenerator.GenerateJson(ScriptApiKnownRegistries.CreateExtractedCapabilities());

        Assert.DoesNotContain("\"propertyName\": \"dom\"", json);
        Assert.DoesNotContain("\"propertyName\": \"tabs\"", json);
        Assert.DoesNotContain("\"propertyName\": \"intercept\"", json);
        Assert.DoesNotContain("\"propertyName\": \"screenshot\"", json);
        Assert.DoesNotContain("\"propertyName\": \"page\"", json);
        Assert.DoesNotContain("\"propertyName\": \"pane\"", json);
        Assert.DoesNotContain("\"propertyName\": \"network\"", json);
        Assert.DoesNotContain("\"propertyName\": \"layout\"", json);
        Assert.DoesNotContain("\"propertyName\": \"clipboard\"", json);
        Assert.DoesNotContain("\"fullName\": \"g.dom.", json);
        Assert.DoesNotContain("\"fullName\": \"g.tabs.", json);
        Assert.DoesNotContain("\"fullName\": \"g.intercept.", json);
    }

    [Fact]
    public void Generate_json_throws_for_invalid_registry()
    {
        var registry = new ScriptApiRegistry
        {
            Groups =
            [
                new ScriptApiGroup { Name = "OneApi", PropertyName = "one" },
                new ScriptApiGroup { Name = "TwoApi", PropertyName = "one" }
            ]
        };

        var exception = Assert.Throws<ArgumentException>(() => ScriptApiManifestGenerator.GenerateJson(registry));
        Assert.Contains("Duplicate group property name 'one'.", exception.Message);
    }
}
