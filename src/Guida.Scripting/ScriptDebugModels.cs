namespace Guida.Scripting;

/// <summary>
/// Reason script execution paused.
/// </summary>
public enum PauseReason
{
    /// <summary>
    /// Execution paused after a step operation.
    /// </summary>
    Step,

    /// <summary>
    /// Execution paused at a breakpoint.
    /// </summary>
    Breakpoint,

    /// <summary>
    /// Execution paused because of an exception.
    /// </summary>
    Exception,

    /// <summary>
    /// Execution paused at a language-level debugger statement.
    /// </summary>
    DebuggerStatement,

    /// <summary>
    /// Execution paused around a host API call.
    /// </summary>
    ApiCall
}

/// <summary>
/// Location within a script.
/// </summary>
public sealed record ScriptLocation(string Source, int Line, int Column)
{
    /// <inheritdoc />
    public override string ToString() => $"{Source}:{Line}:{Column}";
}

/// <summary>
/// A breakpoint set in a script.
/// </summary>
public sealed record ScriptBreakpoint(int Line, int? Column = null, string? Condition = null)
{
    /// <summary>
    /// Whether the breakpoint should be cleared after it is hit.
    /// </summary>
    public bool IsTemporary { get; init; }
}

/// <summary>
/// A frame in a script call stack.
/// </summary>
public sealed record ScriptStackFrame(string FunctionName, ScriptLocation Location);

/// <summary>
/// A script variable visible to the debugger.
/// </summary>
public sealed record ScriptVariable(string Name, object? Value, string TypeName)
{
    /// <summary>
    /// Display text for the variable value.
    /// </summary>
    public string DisplayValue => Value?.ToString() ?? "null";
}

/// <summary>
/// Information provided when script execution pauses.
/// </summary>
public sealed record ScriptPauseInfo
{
    /// <summary>
    /// Location where execution paused.
    /// </summary>
    public required ScriptLocation Location { get; init; }

    /// <summary>
    /// Reason execution paused.
    /// </summary>
    public required PauseReason Reason { get; init; }

    /// <summary>
    /// Source lines near the pause location.
    /// </summary>
    public IReadOnlyList<string> SourceLines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Host API call name when the pause is related to an API call.
    /// </summary>
    public string? ApiCallName { get; init; }

    /// <summary>
    /// Host API call arguments when available.
    /// </summary>
    public string? ApiCallArgs { get; init; }

    /// <summary>
    /// Error message when the pause is related to a failure.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Variables available at the pause location.
    /// </summary>
    public IReadOnlyList<ScriptVariable> Variables { get; init; } = Array.Empty<ScriptVariable>();

    /// <summary>
    /// Call stack at the pause location.
    /// </summary>
    public IReadOnlyList<ScriptStackFrame> CallStack { get; init; } = Array.Empty<ScriptStackFrame>();
}
