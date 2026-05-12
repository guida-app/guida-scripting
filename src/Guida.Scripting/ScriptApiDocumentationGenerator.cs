using System.Text;

namespace Guida.Scripting;

/// <summary>
/// Options for generating public script API documentation from a registry.
/// </summary>
public sealed record ScriptApiDocumentationOptions
{
    public string Title { get; init; } = "Guida Scripting API";

    public bool IncludeTypeAliases { get; init; } = true;

    public bool IncludeInterfaces { get; init; } = true;

    public bool IncludeGroups { get; init; } = true;
}

/// <summary>
/// Generates deterministic Markdown documentation from public script API registry metadata.
/// </summary>
public static class ScriptApiDocumentationGenerator
{
    public static string GenerateMarkdown(
        ScriptApiRegistry registry,
        ScriptApiDocumentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var validationErrors = registry.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Script API registry is invalid: {string.Join("; ", validationErrors)}",
                nameof(registry));
        }

        var generationOptions = options ?? new ScriptApiDocumentationOptions();
        if (string.IsNullOrWhiteSpace(generationOptions.Title))
        {
            throw new ArgumentException("Documentation title cannot be empty.", nameof(options));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {generationOptions.Title}");
        sb.AppendLine();
        sb.AppendLine("Generated from public `ScriptApiRegistry` metadata.");
        sb.AppendLine();

        if (generationOptions.IncludeTypeAliases && registry.TypeAliases.Count > 0)
        {
            AppendTypeAliases(sb, registry.TypeAliases);
        }

        if (generationOptions.IncludeInterfaces && registry.Interfaces.Count > 0)
        {
            AppendInterfaces(sb, registry.Interfaces);
        }

        if (generationOptions.IncludeGroups && registry.Groups.Count > 0)
        {
            AppendGroups(sb, registry.Groups);
        }

        if (registry.Functions.Count > 0)
        {
            AppendTopLevelFunctions(sb, registry.Functions);
        }

        return sb.ToString();
    }

    private static void AppendTypeAliases(StringBuilder sb, IReadOnlyList<ScriptApiTypeAlias> aliases)
    {
        sb.AppendLine("## Type Aliases");
        sb.AppendLine();

        foreach (var alias in aliases.OrderBy(alias => alias.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"### `{alias.Name}`");
            AppendDescription(sb, alias.Description);
            sb.AppendLine();
            sb.AppendLine("```ts");
            sb.AppendLine($"type {alias.Name} = {alias.Definition};");
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static void AppendInterfaces(StringBuilder sb, IReadOnlyList<ScriptApiInterface> interfaces)
    {
        sb.AppendLine("## Data Interfaces");
        sb.AppendLine();

        foreach (var iface in interfaces.OrderBy(iface => iface.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"### `{iface.Name}`");
            AppendDescription(sb, iface.Description);
            AppendPropertiesTable(sb, iface.Properties);
            AppendFunctionsTable(sb, iface.Functions);
            sb.AppendLine();
        }
    }

    private static void AppendGroups(StringBuilder sb, IReadOnlyList<ScriptApiGroup> groups)
    {
        sb.AppendLine("## API Groups");
        sb.AppendLine();

        foreach (var group in groups.OrderBy(group => group.PropertyName, StringComparer.Ordinal))
        {
            sb.AppendLine($"### `g.{group.PropertyName}`");
            AppendDescription(sb, group.Description);
            sb.AppendLine();
            sb.AppendLine($"Type: `{group.Name}`");
            sb.AppendLine();
            AppendPropertiesTable(sb, group.Properties);
            AppendFunctionsTable(sb, group.Functions);
            sb.AppendLine();
        }
    }

    private static void AppendTopLevelFunctions(StringBuilder sb, IReadOnlyList<ScriptApiFunction> functions)
    {
        sb.AppendLine("## Top-Level Functions");
        sb.AppendLine();
        AppendFunctionsTable(sb, functions.OrderBy(function => function.FullName, StringComparer.Ordinal).ToList());
    }

    private static void AppendPropertiesTable(StringBuilder sb, IReadOnlyList<ScriptApiProperty> properties)
    {
        if (properties.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("| Property | Type | Description |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            var optional = property.Optional ? "?" : string.Empty;
            sb.AppendLine($"| `{property.Name}{optional}` | `{property.Type.ToTypeString()}` | {EscapeTable(property.Description)} |");
        }
    }

    private static void AppendFunctionsTable(StringBuilder sb, IReadOnlyList<ScriptApiFunction> functions)
    {
        if (functions.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("| Function | Returns | Description |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var function in functions.OrderBy(function => function.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine($"| `{ToSignature(function)}` | `{function.ReturnType.ToTypeString()}` | {EscapeTable(function.Description)} |");
        }
    }

    private static string ToSignature(ScriptApiFunction function)
    {
        var parameters = string.Join(", ", function.Parameters.Select(parameter =>
            $"{parameter.Name}{(parameter.Optional ? "?" : string.Empty)}: {parameter.Type.ToTypeString()}"));

        return $"{function.FullName}({parameters})";
    }

    private static void AppendDescription(StringBuilder sb, string? description)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine();
            sb.AppendLine(description);
        }
    }

    private static string EscapeTable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|", StringComparison.Ordinal);
}
