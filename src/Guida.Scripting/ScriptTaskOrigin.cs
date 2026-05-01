namespace Guida.Scripting;

/// <summary>
/// Identifies where a script task came from.
/// </summary>
public enum ScriptTaskOrigin
{
    /// <summary>
    /// The task was started directly by a user.
    /// </summary>
    User = 0,

    /// <summary>
    /// The task was started by the host application.
    /// </summary>
    Host,

    /// <summary>
    /// The task was started by a worker.
    /// </summary>
    Worker,

    /// <summary>
    /// The task was started from a queue.
    /// </summary>
    Queue,

    /// <summary>
    /// The task was started in response to an event.
    /// </summary>
    Event,

    /// <summary>
    /// The task was started by system runtime behavior.
    /// </summary>
    System,

    /// <summary>
    /// The task was started by a Model Context Protocol integration.
    /// </summary>
    Mcp,

    /// <summary>
    /// The task was started while handling intercepted host activity.
    /// </summary>
    Intercept,

    /// <summary>
    /// The task was started by an external source.
    /// </summary>
    External
}
