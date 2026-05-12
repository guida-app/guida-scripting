using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guida.Scripting;

/// <summary>
/// Stable error codes for expected workflow ledger failures.
/// </summary>
public enum ScriptWorkflowLedgerErrorCode
{
    /// <summary>The workflow name is invalid.</summary>
    InvalidWorkflowName,

    /// <summary>The workflow run id is invalid.</summary>
    InvalidRunId,

    /// <summary>The workflow item id is invalid.</summary>
    InvalidItemId,

    /// <summary>The workflow item key is invalid.</summary>
    InvalidItemKey,

    /// <summary>The event input is invalid.</summary>
    InvalidEvent,

    /// <summary>The artifact input is invalid.</summary>
    InvalidArtifact,

    /// <summary>The request input is invalid.</summary>
    InvalidRequest,

    /// <summary>The requested run, item, event, or artifact was not found.</summary>
    NotFound,

    /// <summary>The requested transition is not allowed.</summary>
    InvalidTransition,

    /// <summary>The requested item is not leased by the expected owner.</summary>
    LeaseMismatch,

    /// <summary>The workflow ledger capability is unavailable.</summary>
    Unavailable,

    /// <summary>The host denied access to the requested workflow ledger data.</summary>
    AccessDenied,

    /// <summary>A host I/O failure occurred.</summary>
    IoError
}

/// <summary>
/// Describes an expected workflow ledger operation failure.
/// </summary>
public sealed record ScriptWorkflowLedgerError
{
    /// <summary>
    /// Creates a workflow ledger error.
    /// </summary>
    public ScriptWorkflowLedgerError(
        ScriptWorkflowLedgerErrorCode code,
        string workflowName,
        string runId,
        string itemId,
        string message)
    {
        ArgumentNullException.ThrowIfNull(workflowName);
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        WorkflowName = workflowName;
        RunId = runId;
        ItemId = itemId;
        Message = message;
    }

    /// <summary>Stable workflow ledger error code.</summary>
    public ScriptWorkflowLedgerErrorCode Code { get; }

    /// <summary>Workflow name related to the error, or empty when not applicable.</summary>
    public string WorkflowName { get; }

    /// <summary>Run id related to the error, or empty when not applicable.</summary>
    public string RunId { get; }

    /// <summary>Item id related to the error, or empty when not applicable.</summary>
    public string ItemId { get; }

    /// <summary>Host-readable error message.</summary>
    public string Message { get; }

    /// <summary>
    /// Converts the workflow ledger error into a failed execution result.
    /// </summary>
    public ScriptExecutionResult ToExecutionResult() => ScriptExecutionResult.Failed(Message);
}

/// <summary>
/// Result of a workflow ledger operation that does not return a value.
/// </summary>
public sealed record ScriptWorkflowLedgerResult
{
    private ScriptWorkflowLedgerResult(bool success, ScriptWorkflowLedgerError? error)
    {
        Success = success;
        Error = error;
    }

    /// <summary>Whether the workflow ledger operation succeeded.</summary>
    public bool Success { get; }

    /// <summary>Error information when the operation failed.</summary>
    public ScriptWorkflowLedgerError? Error { get; }

    /// <summary>Creates a successful workflow ledger result.</summary>
    public static ScriptWorkflowLedgerResult Succeeded() => new(true, null);

    /// <summary>Creates a failed workflow ledger result.</summary>
    public static ScriptWorkflowLedgerResult Failed(ScriptWorkflowLedgerError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScriptWorkflowLedgerResult(false, error);
    }
}

/// <summary>
/// Result of a workflow ledger operation that returns a value.
/// </summary>
public sealed record ScriptWorkflowLedgerResult<T>
{
    private ScriptWorkflowLedgerResult(bool success, T? value, ScriptWorkflowLedgerError? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the workflow ledger operation succeeded.</summary>
    public bool Success { get; }

    /// <summary>Returned value when the operation succeeded.</summary>
    public T? Value { get; }

    /// <summary>Error information when the operation failed.</summary>
    public ScriptWorkflowLedgerError? Error { get; }

    /// <summary>Creates a successful workflow ledger result.</summary>
    public static ScriptWorkflowLedgerResult<T> Succeeded(T value) => new(true, value, null);

    /// <summary>Creates a failed workflow ledger result.</summary>
    public static ScriptWorkflowLedgerResult<T> Failed(ScriptWorkflowLedgerError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ScriptWorkflowLedgerResult<T>(false, default, error);
    }
}

/// <summary>A durable workflow run snapshot.</summary>
public sealed record ScriptWorkflowRun
{
    public string Id { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Source { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? LastError { get; init; }
    public string? MetadataJson { get; init; }
}

/// <summary>A durable workflow item snapshot.</summary>
public sealed record ScriptWorkflowItem
{
    public string Id { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public string ItemKey { get; init; } = string.Empty;
    public string? ItemType { get; init; }
    public string? RunId { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int Priority { get; init; }
    public int AttemptCount { get; init; }
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public string? LastError { get; init; }
    public string? LastErrorType { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? MetadataJson { get; init; }
}

/// <summary>A durable workflow item event.</summary>
public sealed record ScriptWorkflowEvent
{
    public string Id { get; init; } = string.Empty;
    public string? RunId { get; init; }
    public string? ItemId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? Stage { get; init; }
    public string? State { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? MetadataJson { get; init; }
}

/// <summary>A durable workflow artifact reference.</summary>
public sealed record ScriptWorkflowArtifact
{
    public string Id { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string? EventId { get; init; }
    public string ArtifactKind { get; init; } = string.Empty;
    public string ArtifactRef { get; init; } = string.Empty;
    public string? Role { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowRunOptions
{
    public string? Source { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowRunQuery
{
    public string? WorkflowName { get; init; }
    public string? Status { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
}

public sealed record ScriptWorkflowRunFinishOptions
{
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowRunFailOptions
{
    public string? Error { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowRunCancelOptions
{
    public string? Reason { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowItemUpsert
{
    public string? WorkflowName { get; init; }
    public string? ItemKey { get; init; }
    public string? ItemType { get; init; }
    public string? RunId { get; init; }
    public string? Stage { get; init; }
    public string? State { get; init; }
    public int Priority { get; init; }
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowItemQuery
{
    public string? WorkflowName { get; init; }
    public string? RunId { get; init; }
    public string? Stage { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? RetryReadyAt { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 100;
}

public sealed record ScriptWorkflowStateUpdate
{
    public string? Stage { get; init; }
    public string? State { get; init; }
    public int? Priority { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public string? LastError { get; init; }
    public string? LastErrorType { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowEventAppend
{
    public string? RunId { get; init; }
    public string? EventType { get; init; }
    public string? Stage { get; init; }
    public string? State { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowEventQuery
{
    public int Skip { get; init; }
    public int Take { get; init; } = 200;
}

public sealed record ScriptWorkflowArtifactAttach
{
    public string? EventId { get; init; }
    public string? ArtifactKind { get; init; }
    public string? ArtifactRef { get; init; }
    public string? Role { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowClaimOptions
{
    public string? LeaseOwner { get; init; }
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public DateTimeOffset? NowUtc { get; init; }
    public int Take { get; init; } = 1;
}

public sealed record ScriptWorkflowItemCompleteOptions
{
    public string? LeaseOwner { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowItemFailureOptions
{
    public string? Error { get; init; }
    public string? ErrorType { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? MetadataJson { get; init; }
}

public sealed record ScriptWorkflowBulkMutationRequest
{
    public IReadOnlyList<string>? ItemIds { get; init; }
    public ScriptWorkflowItemQuery? Query { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? MetadataJson { get; init; }
    public int MaxItems { get; init; } = 1000;
}

public sealed record ScriptWorkflowBulkMutationResult(
    int Requested,
    int Matched,
    int Succeeded,
    int Failed,
    IReadOnlyList<ScriptWorkflowItem> Items,
    IReadOnlyList<ScriptWorkflowBulkMutationError> Errors);

public sealed record ScriptWorkflowBulkMutationError(
    string? ItemId,
    string? ItemKey,
    string? WorkflowName,
    string Error);

public sealed record ScriptWorkflowLedgerRetentionOptions
{
    public string? WorkflowName { get; init; }
    public DateTimeOffset? OlderThanUtc { get; init; }
    public bool IncludeStandaloneTerminalItems { get; init; } = true;
    public bool Vacuum { get; init; }
}

public sealed record ScriptWorkflowLedgerRetentionResult(
    int RunsDeleted,
    int ItemsDeleted,
    int EventsDeleted,
    int ArtifactsDeleted,
    bool Vacuumed,
    bool DryRun);

public sealed record ScriptWorkflowLedgerExportOptions
{
    public string? WorkflowName { get; init; }
    public string? RunId { get; init; }
    public IReadOnlyList<string>? ItemIds { get; init; }
    public bool IncludeEvents { get; init; } = true;
    public bool IncludeArtifacts { get; init; } = true;
    public int Take { get; init; } = 1000;
}

public sealed record ScriptWorkflowLedgerExport
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<ScriptWorkflowRun> Runs { get; init; } = Array.Empty<ScriptWorkflowRun>();
    public IReadOnlyList<ScriptWorkflowItem> Items { get; init; } = Array.Empty<ScriptWorkflowItem>();
    public IReadOnlyList<ScriptWorkflowEvent> Events { get; init; } = Array.Empty<ScriptWorkflowEvent>();
    public IReadOnlyList<ScriptWorkflowArtifact> Artifacts { get; init; } = Array.Empty<ScriptWorkflowArtifact>();
}

public sealed record ScriptWorkflowLedgerImportResult(
    int RunsInserted,
    int RunsSkipped,
    int ItemsInserted,
    int ItemsSkipped,
    int EventsInserted,
    int EventsSkipped,
    int ArtifactsInserted,
    int ArtifactsSkipped,
    int Errors,
    IReadOnlyList<string> ErrorMessages);

public sealed record ScriptWorkflowLedgerOverviewQuery
{
    public string? WorkflowName { get; init; }
    public string? RunId { get; init; }
    public string? ItemId { get; init; }
    public DateTimeOffset? NowUtc { get; init; }
    public int AttentionTake { get; init; } = 50;
}

public sealed record ScriptWorkflowLedgerOverview(
    int TotalRuns,
    int TotalItems,
    IReadOnlyDictionary<string, int> RunCountsByStatus,
    IReadOnlyDictionary<string, int> ItemCountsByState,
    IReadOnlyDictionary<string, int> ItemCountsByStage,
    int RetryReadyCount,
    int ActiveLeaseCount,
    int ExpiredLeaseCount,
    IReadOnlyList<ScriptWorkflowLedgerAttentionItem> AttentionItems);

public sealed record ScriptWorkflowLedgerAttentionItem(
    string Kind,
    string Severity,
    string WorkflowName,
    string? RunId,
    string? ItemId,
    string? ItemKey,
    string? Stage,
    string? State,
    string Message,
    DateTimeOffset Timestamp,
    string? LastError);

public sealed record ScriptWorkflowTransitionGraphQuery
{
    public string? WorkflowName { get; init; }
    public string? RunId { get; init; }
    public int Take { get; init; } = 1000;
}

public sealed record ScriptWorkflowTransitionGraph(
    int TotalItems,
    int TotalTransitions,
    IReadOnlyList<ScriptWorkflowTransitionNode> Nodes,
    IReadOnlyList<ScriptWorkflowTransitionEdge> Edges,
    string SchemaStatus = ScriptWorkflowTransitionSchemaStatus.Unknown,
    string? SchemaMessage = null,
    int AllowedTransitionCount = 0,
    int UnexpectedTransitionCount = 0,
    int SchemaOnlyTransitionCount = 0);

public sealed record ScriptWorkflowTransitionNode(
    string Id,
    string Stage,
    string State,
    int CurrentItemCount,
    int EventCount,
    int ErrorCount,
    string SchemaStatus = ScriptWorkflowTransitionSchemaStatus.Observed);

public sealed record ScriptWorkflowTransitionEdge(
    string FromId,
    string ToId,
    string FromStage,
    string FromState,
    string ToStage,
    string ToState,
    int Count,
    int ErrorCount,
    int RetryCount,
    int DeadLetterCount,
    DateTimeOffset? LastSeenAt,
    string? SampleItemId,
    string SchemaStatus = ScriptWorkflowTransitionSchemaStatus.Observed);

public static class ScriptWorkflowTransitionSchemaStatus
{
    public const string Unknown = "unknown";
    public const string NoWorkflowSelected = "no_workflow_selected";
    public const string NoSchema = "no_schema";
    public const string InvalidSchema = "invalid_schema";
    public const string ActiveSchema = "active_schema";
    public const string Observed = "observed";
    public const string Allowed = "allowed";
    public const string Unexpected = "unexpected";
    public const string SchemaOnly = "schema_only";
}

public sealed record ScriptWorkflowFlowEvidenceQuery
{
    public string? WorkflowName { get; init; }
    public string? RunId { get; init; }
    public int Take { get; init; } = 1000;
}

public sealed record ScriptWorkflowFlowEvidence(
    string? WorkflowName,
    string? RunId,
    int TotalItems,
    IReadOnlyDictionary<string, int> ItemCountsByState,
    IReadOnlyDictionary<string, ScriptWorkflowFlowQueueEvidence> Queues);

public sealed record ScriptWorkflowFlowQueueEvidence(
    string QueueName,
    int ItemCount,
    IReadOnlyDictionary<string, int> ItemCountsByState,
    int ProblemCount,
    DateTimeOffset? LastSeenAt,
    string? SampleItemId);

/// <summary>
/// Validates workflow ledger item creation and transitions.
/// </summary>
public interface IScriptWorkflowLedgerTransitionValidator
{
    ScriptWorkflowLedgerResult ValidateCreation(string workflowName, string itemKey, string stage, string state);
    ScriptWorkflowLedgerResult ValidateTransition(ScriptWorkflowItem current, string toStage, string toState, string operation);
}

/// <summary>
/// Provides workflow ledger schema details for diagnostic or read-model overlays.
/// </summary>
public interface IScriptWorkflowLedgerSchemaProvider
{
    bool TryGetSchema(string workflowName, out ScriptWorkflowLedgerSchema schema);
    bool TryGetInvalidSchemaError(string workflowName, out string error);
}

/// <summary>
/// Workflow ledger schema used for transition validation.
/// </summary>
public sealed class ScriptWorkflowLedgerSchema
{
    public int Version { get; init; }
    public List<string>? Stages { get; init; }
    public List<string>? States { get; init; }
    public List<ScriptWorkflowLedgerTransition> Transitions { get; init; } = [];

    [JsonIgnore]
    internal HashSet<string>? StageSet { get; private set; }

    [JsonIgnore]
    internal HashSet<string>? StateSet { get; private set; }

    internal void NormalizeAndValidate(string workflowName)
    {
        if (Version != 1)
        {
            throw new ArgumentException($"Workflow '{workflowName}' schema version must be 1.");
        }

        StageSet = BuildSet(Stages, "stages");
        StateSet = BuildSet(States, "states");

        foreach (var transition in Transitions)
        {
            transition.NormalizeAndValidate();
            if (transition.FromStage != "*" && StageSet != null && !StageSet.Contains(transition.FromStage!))
            {
                throw new ArgumentException($"Transition fromStage '{transition.FromStage}' is not listed in stages.");
            }

            if (transition.ToStage != "*" && StageSet != null && !StageSet.Contains(transition.ToStage!))
            {
                throw new ArgumentException($"Transition toStage '{transition.ToStage}' is not listed in stages.");
            }

            if (transition.FromState != "*" && StateSet != null && !StateSet.Contains(transition.FromState!))
            {
                throw new ArgumentException($"Transition fromState '{transition.FromState}' is not listed in states.");
            }

            if (transition.ToState != "*" && StateSet != null && !StateSet.Contains(transition.ToState!))
            {
                throw new ArgumentException($"Transition toState '{transition.ToState}' is not listed in states.");
            }
        }
    }

    internal ScriptWorkflowLedgerResult ValidateStageAndState(
        string workflowName,
        string itemKey,
        string stage,
        string state)
    {
        if (StageSet != null && !StageSet.Contains(stage))
        {
            return Failed(
                ScriptWorkflowLedgerErrorCode.InvalidTransition,
                workflowName,
                string.Empty,
                $"Workflow '{workflowName}' schema rejects item '{itemKey}' stage '{stage}'.");
        }

        if (StateSet != null && !StateSet.Contains(state))
        {
            return Failed(
                ScriptWorkflowLedgerErrorCode.InvalidTransition,
                workflowName,
                string.Empty,
                $"Workflow '{workflowName}' schema rejects item '{itemKey}' state '{state}'.");
        }

        return ScriptWorkflowLedgerResult.Succeeded();
    }

    internal bool AllowsTransition(string fromStage, string fromState, string toStage, string toState)
    {
        return Transitions.Count == 0 ||
            Transitions.Any(transition => transition.Matches(fromStage, fromState, toStage, toState));
    }

    private static HashSet<string>? BuildSet(IEnumerable<string>? values, string propertyName)
    {
        if (values == null)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = TrimToNull(value);
            if (normalized == null)
            {
                throw new ArgumentException($"{propertyName} cannot contain empty values.");
            }

            set.Add(normalized);
        }

        return set;
    }

    private static ScriptWorkflowLedgerResult Failed(
        ScriptWorkflowLedgerErrorCode code,
        string workflowName,
        string itemId,
        string message) =>
        ScriptWorkflowLedgerResult.Failed(new ScriptWorkflowLedgerError(code, workflowName, string.Empty, itemId, message));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// A workflow ledger schema transition rule.
/// </summary>
public sealed class ScriptWorkflowLedgerTransition
{
    public string? FromStage { get; set; }
    public string? FromState { get; set; }
    public string? ToStage { get; set; }
    public string? ToState { get; set; }

    internal bool IsConcrete =>
        FromStage != "*" &&
        FromState != "*" &&
        ToStage != "*" &&
        ToState != "*";

    internal void NormalizeAndValidate()
    {
        FromStage = Normalize(FromStage, nameof(FromStage));
        FromState = Normalize(FromState, nameof(FromState));
        ToStage = Normalize(ToStage, nameof(ToStage));
        ToState = Normalize(ToState, nameof(ToState));
    }

    internal bool Matches(string fromStage, string fromState, string toStage, string toState)
    {
        return MatchesPart(FromStage, fromStage) &&
            MatchesPart(FromState, fromState) &&
            MatchesPart(ToStage, toStage) &&
            MatchesPart(ToState, toState);
    }

    private static string Normalize(string? value, string propertyName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException($"Transition {propertyName} is required.");
        }

        return normalized;
    }

    private static bool MatchesPart(string? pattern, string value) =>
        pattern == "*" || string.Equals(pattern, value, StringComparison.Ordinal);
}

/// <summary>
/// Schema-backed workflow ledger transition validator.
/// </summary>
public sealed class ScriptWorkflowLedgerSchemaValidator :
    IScriptWorkflowLedgerTransitionValidator,
    IScriptWorkflowLedgerSchemaProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyDictionary<string, ScriptWorkflowLedgerSchema> _schemas;
    private readonly IReadOnlyDictionary<string, string> _invalidSchemas;

    public ScriptWorkflowLedgerSchemaValidator(
        IReadOnlyDictionary<string, ScriptWorkflowLedgerSchema>? schemas = null,
        IReadOnlyDictionary<string, string>? invalidSchemas = null)
    {
        _schemas = schemas ?? new Dictionary<string, ScriptWorkflowLedgerSchema>(StringComparer.OrdinalIgnoreCase);
        _invalidSchemas = invalidSchemas ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An unrestricted validator.</summary>
    public static ScriptWorkflowLedgerSchemaValidator Empty { get; } = new();

    /// <summary>
    /// Creates a schema validator from workflow-name keyed schema JSON content.
    /// </summary>
    public static ScriptWorkflowLedgerSchemaValidator FromJsonByWorkflow(IReadOnlyDictionary<string, string> schemaJsonByWorkflow)
    {
        ArgumentNullException.ThrowIfNull(schemaJsonByWorkflow);

        var schemas = new Dictionary<string, ScriptWorkflowLedgerSchema>(StringComparer.OrdinalIgnoreCase);
        var invalid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (workflowName, json) in schemaJsonByWorkflow)
        {
            var normalizedWorkflowName = NormalizeRequired(workflowName, nameof(workflowName));
            try
            {
                var schema = JsonSerializer.Deserialize<ScriptWorkflowLedgerSchema>(json, JsonOptions);
                if (schema == null)
                {
                    throw new JsonException("Schema is empty.");
                }

                schema.NormalizeAndValidate(normalizedWorkflowName);
                schemas[normalizedWorkflowName] = schema;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                invalid[normalizedWorkflowName] = ex.Message;
            }
        }

        return new ScriptWorkflowLedgerSchemaValidator(schemas, invalid);
    }

    public ScriptWorkflowLedgerResult ValidateCreation(string workflowName, string itemKey, string stage, string state)
    {
        var normalizedWorkflowName = NormalizeRequired(workflowName, nameof(workflowName));
        var invalid = GetInvalidSchemaResult(normalizedWorkflowName);
        if (!invalid.Success)
        {
            return invalid;
        }

        if (!_schemas.TryGetValue(normalizedWorkflowName, out var schema))
        {
            return ScriptWorkflowLedgerResult.Succeeded();
        }

        return schema.ValidateStageAndState(normalizedWorkflowName, itemKey, stage, state);
    }

    public ScriptWorkflowLedgerResult ValidateTransition(
        ScriptWorkflowItem current,
        string toStage,
        string toState,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(current);

        var workflowName = NormalizeRequired(current.WorkflowName, nameof(current.WorkflowName));
        var invalid = GetInvalidSchemaResult(workflowName);
        if (!invalid.Success)
        {
            return invalid;
        }

        if (!_schemas.TryGetValue(workflowName, out var schema))
        {
            return ScriptWorkflowLedgerResult.Succeeded();
        }

        var stageAndState = schema.ValidateStageAndState(workflowName, current.ItemKey, toStage, toState);
        if (!stageAndState.Success)
        {
            return stageAndState;
        }

        if (string.Equals(current.Stage, toStage, StringComparison.Ordinal) &&
            string.Equals(current.State, toState, StringComparison.Ordinal))
        {
            return ScriptWorkflowLedgerResult.Succeeded();
        }

        if (schema.AllowsTransition(current.Stage, current.State, toStage, toState))
        {
            return ScriptWorkflowLedgerResult.Succeeded();
        }

        return ScriptWorkflowLedgerResult.Failed(new ScriptWorkflowLedgerError(
            ScriptWorkflowLedgerErrorCode.InvalidTransition,
            workflowName,
            current.RunId ?? string.Empty,
            current.Id,
            $"Workflow '{workflowName}' schema rejects {operation} for item '{current.ItemKey}' from '{current.Stage}/{current.State}' to '{toStage}/{toState}'."));
    }

    public bool TryGetSchema(string workflowName, out ScriptWorkflowLedgerSchema schema)
    {
        var normalizedWorkflowName = NormalizeRequired(workflowName, nameof(workflowName));
        return _schemas.TryGetValue(normalizedWorkflowName, out schema!);
    }

    public bool TryGetInvalidSchemaError(string workflowName, out string error)
    {
        var normalizedWorkflowName = NormalizeRequired(workflowName, nameof(workflowName));
        return _invalidSchemas.TryGetValue(normalizedWorkflowName, out error!);
    }

    private ScriptWorkflowLedgerResult GetInvalidSchemaResult(string workflowName)
    {
        return _invalidSchemas.TryGetValue(workflowName, out var error)
            ? ScriptWorkflowLedgerResult.Failed(new ScriptWorkflowLedgerError(
                ScriptWorkflowLedgerErrorCode.InvalidTransition,
                workflowName,
                string.Empty,
                string.Empty,
                $"Workflow '{workflowName}' schema is invalid: {error}"))
            : ScriptWorkflowLedgerResult.Succeeded();
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return trimmed;
    }
}
