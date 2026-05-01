namespace Guida.Scripting;

/// <summary>
/// Represents the outcome of a script execution.
/// </summary>
public sealed record ScriptExecutionResult
{
    /// <summary>
    /// Whether the script completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Values returned by the script runtime.
    /// </summary>
    public IReadOnlyList<object?> ReturnValues { get; init; } = Array.Empty<object?>();

    /// <summary>
    /// A host-readable error message when execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The exception type name when the failure came from an exception.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Stack trace text when available.
    /// </summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// How long execution took.
    /// </summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>
    /// Whether execution was canceled by the host or caller.
    /// </summary>
    public bool IsCanceled { get; init; }

    /// <summary>
    /// Whether execution stopped because its timeout elapsed.
    /// </summary>
    public bool IsTimedOut { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ScriptExecutionResult Succeeded(params object?[] returnValues) =>
        new()
        {
            Success = true,
            ReturnValues = returnValues
        };

    /// <summary>
    /// Creates a failed result from an error message.
    /// </summary>
    public static ScriptExecutionResult Failed(string error, Exception? exception = null) =>
        new()
        {
            Success = false,
            Error = error,
            ExceptionType = exception?.GetType().FullName,
            StackTrace = exception?.StackTrace
        };

    /// <summary>
    /// Creates a canceled result.
    /// </summary>
    public static ScriptExecutionResult Canceled(string? error = null) =>
        new()
        {
            Success = false,
            Error = error,
            IsCanceled = true
        };

    /// <summary>
    /// Creates a timed-out result.
    /// </summary>
    public static ScriptExecutionResult TimedOut(TimeSpan timeout) =>
        new()
        {
            Success = false,
            Error = $"Script execution timed out after {timeout}.",
            IsTimedOut = true
        };
}
