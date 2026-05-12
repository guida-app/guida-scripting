using System.Text;

namespace Guida.Scripting;

/// <summary>
/// Generates TypeScript declaration text from public script API registry metadata.
/// </summary>
public static class ScriptApiTypeScriptGenerator
{
    /// <summary>
    /// Generates deterministic TypeScript declaration text.
    /// </summary>
    public static string Generate(
        ScriptApiRegistry registry,
        ScriptApiTypeScriptGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var validationErrors = registry.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Script API registry is invalid: {string.Join("; ", validationErrors)}",
                nameof(registry));
        }

        var generationOptions = options ?? new ScriptApiTypeScriptGeneratorOptions();
        ValidateOptions(generationOptions);

        var sb = new StringBuilder();

        if (generationOptions.IncludeHeader)
        {
            sb.AppendLine("/**");
            sb.AppendLine($" * {generationOptions.Title}");
            sb.AppendLine(" * Auto-generated from ScriptApiRegistry - do not edit manually.");
            sb.AppendLine(" * Output is deterministic and contains only public registry metadata.");
            sb.AppendLine(" */");
            sb.AppendLine();
        }

        AppendTypeAliases(sb, registry.TypeAliases, generationOptions);
        AppendInterfaces(sb, registry.Interfaces, generationOptions);
        AppendGroups(sb, registry.Groups, generationOptions);
        AppendRootInterface(sb, registry, generationOptions);
        AppendGlobalDeclaration(sb, generationOptions);

        return sb.ToString();
    }

    private static void AppendTypeAliases(
        StringBuilder sb,
        IReadOnlyList<ScriptApiTypeAlias> aliases,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        if (aliases.Count == 0)
        {
            return;
        }

        sb.AppendLine("// Type Aliases");
        foreach (var alias in aliases)
        {
            AppendDescription(sb, alias.Description, 0, options);
            sb.AppendLine($"type {alias.Name} = {alias.Definition};");
            sb.AppendLine();
        }
    }

    private static void AppendInterfaces(
        StringBuilder sb,
        IReadOnlyList<ScriptApiInterface> interfaces,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        if (interfaces.Count == 0)
        {
            return;
        }

        sb.AppendLine("// Interfaces");
        foreach (var descriptor in interfaces)
        {
            AppendInterface(sb, descriptor.Name, descriptor.Description, descriptor.Properties, descriptor.Functions, descriptor.Extends, options);
            sb.AppendLine();
        }
    }

    private static void AppendGroups(
        StringBuilder sb,
        IReadOnlyList<ScriptApiGroup> groups,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        if (groups.Count == 0)
        {
            return;
        }

        sb.AppendLine("// API Groups");
        foreach (var group in groups)
        {
            AppendInterface(sb, group.Name, group.Description, group.Properties, group.Functions, extends: null, options);
            sb.AppendLine();
        }
    }

    private static void AppendRootInterface(
        StringBuilder sb,
        ScriptApiRegistry registry,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        AppendDescription(sb, "Main scripting API interface.", 0, options);
        sb.AppendLine($"interface {options.RootInterfaceName} {{");

        foreach (var function in registry.Functions)
        {
            AppendFunction(sb, function, options);
        }

        foreach (var group in registry.Groups)
        {
            if (registry.Functions.Count > 0 || !ReferenceEquals(group, registry.Groups[0]))
            {
                sb.AppendLine();
            }

            AppendDescription(sb, group.Description, 1, options);
            sb.AppendLine($"  {group.PropertyName}: {group.Name};");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void AppendGlobalDeclaration(
        StringBuilder sb,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        AppendDescription(sb, "Global scripting API instance.", 0, options);
        sb.AppendLine($"declare const {options.GlobalVariableName}: {options.RootInterfaceName};");
    }

    private static void AppendInterface(
        StringBuilder sb,
        string name,
        string? description,
        IReadOnlyList<ScriptApiProperty> properties,
        IReadOnlyList<ScriptApiFunction> functions,
        string? extends,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        AppendDescription(sb, description, 0, options);

        var extendsClause = string.IsNullOrEmpty(extends) ? string.Empty : $" extends {extends}";
        sb.AppendLine($"interface {name}{extendsClause} {{");

        foreach (var property in properties)
        {
            AppendDescription(sb, property.Description, 1, options);
            sb.AppendLine($"  {property.ToTypeString()};");
        }

        foreach (var function in functions)
        {
            AppendFunction(sb, function, options);
        }

        sb.AppendLine("}");
    }

    private static void AppendFunction(
        StringBuilder sb,
        ScriptApiFunction function,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        AppendDescription(sb, function.Description, 1, options);
        sb.AppendLine(function.ToDeclarationString());
    }

    private static void AppendDescription(
        StringBuilder sb,
        string? description,
        int indentLevel,
        ScriptApiTypeScriptGeneratorOptions options)
    {
        if (!options.IncludeDescriptions || string.IsNullOrEmpty(description))
        {
            return;
        }

        var indent = new string(' ', indentLevel * 2);
        sb.AppendLine($"{indent}/** {description} */");
    }

    private static void ValidateOptions(ScriptApiTypeScriptGeneratorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootInterfaceName))
        {
            throw new ArgumentException("Root interface name cannot be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.GlobalVariableName))
        {
            throw new ArgumentException("Global variable name cannot be empty.", nameof(options));
        }
    }
}
