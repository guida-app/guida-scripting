using System.Collections.ObjectModel;
using System.Text;

namespace Guida.Scripting;

/// <summary>
/// Compact completion metadata for one script API function.
/// </summary>
public sealed record ScriptApiCompletionItem(
    string FullName,
    string Signature,
    string Description,
    string ReturnType,
    bool IsAsync);

/// <summary>
/// Generates completion, hover, and signature-help metadata from a public script API registry.
/// </summary>
public static class ScriptApiCompletionGenerator
{
    public static IReadOnlyList<ScriptApiCompletionItem> GenerateCompletions(ScriptApiRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var validationErrors = registry.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Script API registry is invalid: {string.Join("; ", validationErrors)}",
                nameof(registry));
        }

        return registry.GetAllFunctions()
            .OrderBy(function => function.FullName, StringComparer.Ordinal)
            .Select(function => new ScriptApiCompletionItem(
                function.FullName,
                function.ToSignatureString(),
                FormatDescription(function),
                function.ReturnType.ToTypeString(),
                function.IsAsync))
            .ToList();
    }

    public static IReadOnlyDictionary<string, string> GenerateHoverDocs(ScriptApiRegistry registry)
    {
        var docs = GenerateCompletions(registry)
            .ToDictionary(
                completion => completion.FullName,
                completion => completion.Description,
                StringComparer.Ordinal);

        docs["g"] = "Global scripting API namespace.";
        foreach (var group in registry.Groups.OrderBy(group => group.PropertyName, StringComparer.Ordinal))
        {
            docs[$"g.{group.PropertyName}"] = group.Description ?? group.Name;
        }

        return new ReadOnlyDictionary<string, string>(docs);
    }

    public static IReadOnlyDictionary<string, ScriptApiFunction> BuildFunctionLookup(ScriptApiRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var validationErrors = registry.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(
                $"Script API registry is invalid: {string.Join("; ", validationErrors)}",
                nameof(registry));
        }

        return new ReadOnlyDictionary<string, ScriptApiFunction>(
            registry.GetAllFunctions()
                .OrderBy(function => function.FullName, StringComparer.Ordinal)
                .ToDictionary(function => function.FullName, StringComparer.Ordinal));
    }

    private static string FormatDescription(ScriptApiFunction function)
    {
        var sb = new StringBuilder();
        sb.AppendLine(function.Description);

        if (function.Parameters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            foreach (var parameter in function.Parameters)
            {
                var optional = parameter.Optional ? " (optional)" : string.Empty;
                var description = parameter.Description ?? parameter.Type.ToTypeString();
                sb.AppendLine($"  {parameter.Name}: {description}{optional}");
            }
        }

        var returnType = function.ReturnType.ToTypeString();
        if (returnType is not "void" and not "Promise<void>")
        {
            sb.AppendLine();
            sb.AppendLine($"Returns: {returnType}");
        }

        return sb.ToString().TrimEnd();
    }
}
