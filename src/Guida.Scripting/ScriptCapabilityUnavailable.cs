namespace Guida.Scripting;

/// <summary>
/// Describes a host capability that was requested but not provided by the host.
/// </summary>
public sealed record ScriptCapabilityUnavailable
{
    /// <summary>
    /// Creates missing-capability information.
    /// </summary>
    public ScriptCapabilityUnavailable(string capabilityName, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        CapabilityName = capabilityName;
        Message = message;
    }

    /// <summary>
    /// Name of the unavailable capability.
    /// </summary>
    public string CapabilityName { get; }

    /// <summary>
    /// Host-readable message describing the unavailable capability.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates missing-capability information for <typeparamref name="TCapability" />.
    /// </summary>
    public static ScriptCapabilityUnavailable For<TCapability>()
        where TCapability : class, IScriptHostCapability
    {
        var capabilityName = typeof(TCapability).FullName ?? typeof(TCapability).Name;
        return new ScriptCapabilityUnavailable(
            capabilityName,
            $"Host capability '{capabilityName}' is unavailable.");
    }

    /// <summary>
    /// Converts the unavailable capability into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
