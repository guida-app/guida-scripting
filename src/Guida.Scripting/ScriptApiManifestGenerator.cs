using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guida.Scripting;

/// <summary>
/// Portable manifest document generated from a public script API registry.
/// </summary>
public sealed record ScriptApiManifestDocument(
    int SchemaVersion,
    string GeneratedFrom,
    IReadOnlyList<ScriptApiManifestTypeAlias> TypeAliases,
    IReadOnlyList<ScriptApiManifestInterface> Interfaces,
    IReadOnlyList<ScriptApiManifestGroup> Groups,
    IReadOnlyList<ScriptApiManifestFunction> Functions);

public sealed record ScriptApiManifestTypeAlias(
    string Name,
    string Definition,
    string? Description);

public sealed record ScriptApiManifestInterface(
    string Name,
    string? Description,
    string? Extends,
    IReadOnlyList<ScriptApiManifestProperty> Properties,
    IReadOnlyList<ScriptApiManifestFunction> Functions);

public sealed record ScriptApiManifestGroup(
    string Name,
    string PropertyName,
    string? Description,
    IReadOnlyList<ScriptApiManifestProperty> Properties,
    IReadOnlyList<ScriptApiManifestFunction> Functions);

public sealed record ScriptApiManifestProperty(
    string Name,
    string Type,
    bool Optional,
    bool ReadOnly,
    string? Description);

public sealed record ScriptApiManifestParameter(
    string Name,
    string Type,
    bool Optional,
    string? Description,
    string? DefaultValue);

public sealed record ScriptApiManifestFunction(
    string Name,
    string FullName,
    string? Namespace,
    string Description,
    bool IsAsync,
    string ReturnType,
    IReadOnlyList<ScriptApiManifestParameter> Parameters);

/// <summary>
/// Generates deterministic portable manifest output from public script API registry metadata.
/// </summary>
public static class ScriptApiManifestGenerator
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a manifest document from a registry.
    /// </summary>
    public static ScriptApiManifestDocument CreateDocument(
        ScriptApiRegistry registry,
        string generatedFrom = "ScriptApiRegistry")
    {
        ArgumentNullException.ThrowIfNull(registry);

        var validationErrors = registry.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Script API registry is invalid: {string.Join("; ", validationErrors)}",
                nameof(registry));
        }

        return new ScriptApiManifestDocument(
            SchemaVersion,
            generatedFrom,
            registry.TypeAliases
                .Select(ToManifestTypeAlias)
                .OrderBy(alias => alias.Name, StringComparer.Ordinal)
                .ToList(),
            registry.Interfaces
                .Select(ToManifestInterface)
                .OrderBy(iface => iface.Name, StringComparer.Ordinal)
                .ToList(),
            registry.Groups
                .Select(ToManifestGroup)
                .OrderBy(group => group.PropertyName, StringComparer.Ordinal)
                .ToList(),
            registry.GetAllFunctions()
                .Select(ToManifestFunction)
                .OrderBy(function => function.FullName, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>
    /// Generates deterministic JSON manifest text.
    /// </summary>
    public static string GenerateJson(
        ScriptApiRegistry registry,
        string generatedFrom = "ScriptApiRegistry")
    {
        var document = CreateDocument(registry, generatedFrom);
        return JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
    }

    private static ScriptApiManifestTypeAlias ToManifestTypeAlias(ScriptApiTypeAlias alias) =>
        new(alias.Name, alias.Definition, alias.Description);

    private static ScriptApiManifestInterface ToManifestInterface(ScriptApiInterface iface) =>
        new(
            iface.Name,
            iface.Description,
            iface.Extends,
            iface.Properties.Select(ToManifestProperty).ToList(),
            iface.Functions.Select(ToManifestFunction).ToList());

    private static ScriptApiManifestGroup ToManifestGroup(ScriptApiGroup group) =>
        new(
            group.Name,
            group.PropertyName,
            group.Description,
            group.Properties.Select(ToManifestProperty).ToList(),
            group.Functions.Select(ToManifestFunction).ToList());

    private static ScriptApiManifestProperty ToManifestProperty(ScriptApiProperty property) =>
        new(
            property.Name,
            property.Type.ToTypeString(),
            property.Optional,
            property.ReadOnly,
            property.Description);

    private static ScriptApiManifestParameter ToManifestParameter(ScriptApiParameter parameter) =>
        new(
            parameter.Name,
            parameter.Type.ToTypeString(),
            parameter.Optional,
            parameter.Description,
            parameter.DefaultValue);

    private static ScriptApiManifestFunction ToManifestFunction(ScriptApiFunction function) =>
        new(
            function.Name,
            function.FullName,
            function.Namespace,
            function.Description,
            function.IsAsync,
            function.ReturnType.ToTypeString(),
            function.Parameters.Select(ToManifestParameter).ToList());
}
