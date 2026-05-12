namespace Guida.Scripting;

/// <summary>
/// Describes an expected script queue operation failure.
/// </summary>
public sealed record ScriptQueueError
{
    /// <summary>
    /// Creates a queue error.
    /// </summary>
    public ScriptQueueError(
        ScriptQueueErrorCode code,
        string queueName,
        string itemId,
        string message)
    {
        ArgumentNullException.ThrowIfNull(queueName);
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        QueueName = queueName;
        ItemId = itemId;
        Message = message;
    }

    /// <summary>
    /// Stable queue error code.
    /// </summary>
    public ScriptQueueErrorCode Code { get; }

    /// <summary>
    /// Queue name related to the error.
    /// </summary>
    public string QueueName { get; }

    /// <summary>
    /// Queue item id related to the error.
    /// </summary>
    public string ItemId { get; }

    /// <summary>
    /// Host-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Converts the queue error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}
