using System.Text;

namespace Guida.Scripting;

/// <summary>
/// Describes one script-facing function parameter.
/// </summary>
public sealed record ScriptApiParameter
{
    public ScriptApiParameter()
    {
    }

    public ScriptApiParameter(string name, ScriptApiType type, bool optional = false, string? description = null)
    {
        Name = name;
        Type = type;
        Optional = optional;
        Description = description;
    }

    public string Name { get; init; } = string.Empty;

    public ScriptApiType Type { get; init; } = ScriptApiType.Any;

    public bool Optional { get; init; }

    public string? Description { get; init; }

    public string? DefaultValue { get; init; }

    public string ToTypeString() => $"{Name}{(Optional ? "?" : string.Empty)}: {Type.ToTypeString()}";
}

/// <summary>
/// Describes one script-facing function.
/// </summary>
public sealed record ScriptApiFunction
{
    public string Name { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string? Namespace { get; init; }

    public IReadOnlyList<ScriptApiParameter> Parameters { get; init; } = Array.Empty<ScriptApiParameter>();

    public ScriptApiType ReturnType { get; init; } = ScriptApiType.Void;

    public string Description { get; init; } = string.Empty;

    public bool IsAsync { get; init; }

    public string ToSignatureString()
    {
        var prefix = IsAsync ? "await " : string.Empty;
        var parameterNames = Parameters.Select(parameter => parameter.Optional ? $"[{parameter.Name}]" : parameter.Name);
        return $"{prefix}{FullName}({string.Join(", ", parameterNames)})";
    }

    public string ToDeclarationString(int indentLevel = 1)
    {
        var indent = new string(' ', indentLevel * 2);
        var parameterTypes = Parameters.Select(parameter => parameter.ToTypeString());
        return $"{indent}{Name}({string.Join(", ", parameterTypes)}): {ReturnType.ToTypeString()};";
    }
}

/// <summary>
/// Describes one property on a script-facing type or API group.
/// </summary>
public sealed record ScriptApiProperty
{
    public ScriptApiProperty()
    {
    }

    public ScriptApiProperty(string name, ScriptApiType type, bool optional = false, string? description = null)
    {
        Name = name;
        Type = type;
        Optional = optional;
        Description = description;
    }

    public string Name { get; init; } = string.Empty;

    public ScriptApiType Type { get; init; } = ScriptApiType.Any;

    public bool Optional { get; init; }

    public bool ReadOnly { get; init; }

    public string? Description { get; init; }

    public string ToTypeString()
    {
        var readOnly = ReadOnly ? "readonly " : string.Empty;
        var optional = Optional ? "?" : string.Empty;
        return $"{readOnly}{Name}{optional}: {Type.ToTypeString()}";
    }
}

/// <summary>
/// Describes a script-facing interface or object type.
/// </summary>
public sealed record ScriptApiInterface
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<ScriptApiProperty> Properties { get; init; } = Array.Empty<ScriptApiProperty>();

    public IReadOnlyList<ScriptApiFunction> Functions { get; init; } = Array.Empty<ScriptApiFunction>();

    public string? Description { get; init; }

    public string? Extends { get; init; }

    public string ToDeclarationString()
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(Description))
        {
            sb.AppendLine("/**");
            sb.AppendLine($" * {Description}");
            sb.AppendLine(" */");
        }

        var extendsClause = !string.IsNullOrEmpty(Extends) ? $" extends {Extends}" : string.Empty;
        sb.AppendLine($"interface {Name}{extendsClause} {{");

        foreach (var property in Properties)
        {
            if (!string.IsNullOrEmpty(property.Description))
            {
                sb.AppendLine($"  /** {property.Description} */");
            }

            sb.AppendLine($"  {property.ToTypeString()};");
        }

        foreach (var function in Functions)
        {
            if (!string.IsNullOrEmpty(function.Description))
            {
                sb.AppendLine($"  /** {function.Description} */");
            }

            sb.AppendLine(function.ToDeclarationString());
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}

/// <summary>
/// Describes a script-facing type alias.
/// </summary>
public sealed record ScriptApiTypeAlias
{
    public ScriptApiTypeAlias()
    {
    }

    public ScriptApiTypeAlias(string name, string definition, string? description = null)
    {
        Name = name;
        Definition = definition;
        Description = description;
    }

    public string Name { get; init; } = string.Empty;

    public string Definition { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string ToDeclarationString()
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(Description))
        {
            sb.AppendLine("/**");
            sb.AppendLine($" * {Description}");
            sb.AppendLine(" */");
        }

        sb.AppendLine($"type {Name} = {Definition};");
        return sb.ToString();
    }
}

/// <summary>
/// Describes one property on the global script API object, such as g.store or g.workflow.
/// </summary>
public sealed record ScriptApiGroup
{
    public string Name { get; init; } = string.Empty;

    public string PropertyName { get; init; } = string.Empty;

    public IReadOnlyList<ScriptApiProperty> Properties { get; init; } = Array.Empty<ScriptApiProperty>();

    public IReadOnlyList<ScriptApiFunction> Functions { get; init; } = Array.Empty<ScriptApiFunction>();

    public string? Description { get; init; }

    public string ToInterfaceString()
    {
        var descriptor = new ScriptApiInterface
        {
            Name = Name,
            Description = Description,
            Properties = Properties,
            Functions = Functions
        };

        return descriptor.ToDeclarationString();
    }
}

/// <summary>
/// Immutable script-facing API registry document.
/// </summary>
public sealed record ScriptApiRegistry
{
    public IReadOnlyList<ScriptApiTypeAlias> TypeAliases { get; init; } = Array.Empty<ScriptApiTypeAlias>();

    public IReadOnlyList<ScriptApiInterface> Interfaces { get; init; } = Array.Empty<ScriptApiInterface>();

    public IReadOnlyList<ScriptApiFunction> Functions { get; init; } = Array.Empty<ScriptApiFunction>();

    public IReadOnlyList<ScriptApiGroup> Groups { get; init; } = Array.Empty<ScriptApiGroup>();

    public IEnumerable<ScriptApiFunction> GetAllFunctions()
    {
        foreach (var function in Functions)
        {
            yield return function;
        }

        foreach (var group in Groups)
        {
            foreach (var function in group.Functions)
            {
                yield return function;
            }
        }

        foreach (var iface in Interfaces)
        {
            foreach (var function in iface.Functions)
            {
                yield return function;
            }
        }
    }

    public ScriptApiFunction? FindFunction(string fullName) =>
        GetAllFunctions().FirstOrDefault(function => string.Equals(function.FullName, fullName, StringComparison.Ordinal));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        AddDuplicateErrors(errors, TypeAliases.Select(alias => alias.Name), "type alias");
        AddDuplicateErrors(errors, Interfaces.Select(iface => iface.Name), "interface");
        AddDuplicateErrors(errors, Groups.Select(group => group.PropertyName), "group property");
        AddDuplicateErrors(errors, GetAllFunctions().Select(function => function.FullName), "function");

        foreach (var group in Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add("API group is missing a type name.");
            }

            if (string.IsNullOrWhiteSpace(group.PropertyName))
            {
                errors.Add($"API group '{group.Name}' is missing a property name.");
            }

            foreach (var function in group.Functions)
            {
                if (!string.Equals(function.Namespace, group.PropertyName, StringComparison.Ordinal))
                {
                    errors.Add($"Function '{function.FullName}' has namespace '{function.Namespace}' but belongs to group '{group.PropertyName}'.");
                }
            }
        }

        foreach (var function in GetAllFunctions())
        {
            if (string.IsNullOrWhiteSpace(function.Name))
            {
                errors.Add("API function is missing a name.");
            }

            if (string.IsNullOrWhiteSpace(function.FullName))
            {
                errors.Add($"API function '{function.Name}' is missing a full name.");
            }

            if (string.IsNullOrWhiteSpace(function.ReturnType.Name))
            {
                errors.Add($"API function '{function.FullName}' is missing a return type.");
            }
        }

        return errors;
    }

    private static void AddDuplicateErrors(List<string> errors, IEnumerable<string> values, string label)
    {
        foreach (var duplicate in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add($"Duplicate {label} name '{duplicate}'.");
        }
    }
}
