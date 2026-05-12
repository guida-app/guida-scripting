namespace Guida.Scripting;

/// <summary>
/// Reference to a host-owned secret.
/// </summary>
public sealed record ScriptSecretReference
{
    /// <summary>
    /// Creates an empty secret reference.
    /// </summary>
    public ScriptSecretReference()
    {
    }

    /// <summary>
    /// Creates a secret reference by name.
    /// </summary>
    public ScriptSecretReference(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Host-defined secret name.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
