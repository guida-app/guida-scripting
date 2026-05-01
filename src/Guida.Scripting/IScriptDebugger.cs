namespace Guida.Scripting;

/// <summary>
/// Provides engine-neutral script debugging capabilities.
/// </summary>
public interface IScriptDebugger
{
    /// <summary>
    /// Whether debugging is enabled for this engine.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Whether execution is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Current script location when execution is paused.
    /// </summary>
    ScriptLocation? CurrentLocation { get; }

    /// <summary>
    /// The script source currently being debugged.
    /// </summary>
    string? ScriptSource { get; }

    /// <summary>
    /// Continues execution until the next pause condition.
    /// </summary>
    void Continue();

    /// <summary>
    /// Steps into the next statement.
    /// </summary>
    void StepInto();

    /// <summary>
    /// Steps over the next statement.
    /// </summary>
    void StepOver();

    /// <summary>
    /// Steps out of the current function.
    /// </summary>
    void StepOut();

    /// <summary>
    /// Stops execution entirely.
    /// </summary>
    void Stop();

    /// <summary>
    /// Returns true when a stop request was observed.
    /// </summary>
    bool WasStopRequested();

    /// <summary>
    /// Sets a breakpoint at the specified line and optional column.
    /// </summary>
    void SetBreakpoint(int line, int? column = null);

    /// <summary>
    /// Clears a breakpoint at the specified line and optional column.
    /// </summary>
    void ClearBreakpoint(int line, int? column = null);

    /// <summary>
    /// Clears all breakpoints.
    /// </summary>
    void ClearAllBreakpoints();

    /// <summary>
    /// Current breakpoints.
    /// </summary>
    IReadOnlyList<ScriptBreakpoint> Breakpoints { get; }

    /// <summary>
    /// Returns the current call stack.
    /// </summary>
    IReadOnlyList<ScriptStackFrame> GetCallStack();

    /// <summary>
    /// Returns local variables in the current scope.
    /// </summary>
    IReadOnlyList<ScriptVariable> GetLocalVariables();

    /// <summary>
    /// Evaluates an expression in the current debug context.
    /// </summary>
    object? Evaluate(string expression);

    /// <summary>
    /// Raised when execution pauses.
    /// </summary>
    event Action<ScriptPauseInfo>? Paused;

    /// <summary>
    /// Raised when execution resumes.
    /// </summary>
    event Action? Resumed;

    /// <summary>
    /// Raised when the current script location changes.
    /// </summary>
    event Action<ScriptLocation>? LineChanged;
}
