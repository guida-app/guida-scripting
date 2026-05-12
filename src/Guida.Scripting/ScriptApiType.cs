namespace Guida.Scripting;

/// <summary>
/// Describes a script-facing type using TypeScript-compatible notation.
/// </summary>
public sealed record ScriptApiType
{
    public ScriptApiType(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Type name or raw type expression.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Whether this type is an array.
    /// </summary>
    public bool IsArray { get; init; }

    /// <summary>
    /// Whether this type accepts null.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Generic type arguments.
    /// </summary>
    public IReadOnlyList<ScriptApiType> GenericArguments { get; init; } = System.Array.Empty<ScriptApiType>();

    public static ScriptApiType String { get; } = new("string");

    public static ScriptApiType Number { get; } = new("number");

    public static ScriptApiType Boolean { get; } = new("boolean");

    public static ScriptApiType Void { get; } = new("void");

    public static ScriptApiType Any { get; } = new("any");

    public static ScriptApiType Unknown { get; } = new("unknown");

    public static ScriptApiType Never { get; } = new("never");

    public static ScriptApiType Null { get; } = new("null");

    public static ScriptApiType Undefined { get; } = new("undefined");

    public static ScriptApiType Custom(string name) => new(name);

    public static ScriptApiType ArrayOf(ScriptApiType inner) => inner with { IsArray = true };

    public static ScriptApiType Nullable(ScriptApiType inner) => inner with { IsNullable = true };

    public static ScriptApiType Generic(string name, params ScriptApiType[] arguments) =>
        new(name) { GenericArguments = arguments };

    public static ScriptApiType Promise(ScriptApiType inner) => Generic("Promise", inner);

    public static ScriptApiType Record(ScriptApiType keyType, ScriptApiType valueType) =>
        Custom($"Record<{keyType.ToTypeString()}, {valueType.ToTypeString()}>");

    /// <summary>
    /// Converts the descriptor to TypeScript-compatible notation.
    /// </summary>
    public string ToTypeString()
    {
        var result = Name;

        if (GenericArguments.Count > 0 && !Name.Contains('<', StringComparison.Ordinal))
        {
            result = $"{Name}<{string.Join(", ", GenericArguments.Select(argument => argument.ToTypeString()))}>";
        }

        if (IsArray)
        {
            result = $"{result}[]";
        }

        if (IsNullable)
        {
            result = $"{result} | null";
        }

        return result;
    }

    public override string ToString() => ToTypeString();
}
