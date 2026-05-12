using System.Collections.ObjectModel;
using System.Text.Json;

namespace Guida.Scripting;

/// <summary>
/// In-memory workflow ledger for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemoryWorkflowLedger : IScriptWorkflowLedger, IScriptWorkflowLedgerAdministration
{
    private const int MaxTake = 1000;

    private readonly IScriptWorkflowLedgerTransitionValidator _transitionValidator;
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemState> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _itemIdsByKey = new(StringComparer.Ordinal);
    private readonly List<EventState> _events = [];
    private readonly Dictionary<string, string> _eventIdsByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly List<ArtifactState> _artifacts = [];
    private readonly Dictionary<string, string> _artifactIdsByIdentity = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _sequence;

    /// <summary>
    /// Creates an unrestricted in-memory workflow ledger.
    /// </summary>
    public ScriptInMemoryWorkflowLedger()
        : this(null)
    {
    }

    /// <summary>
    /// Creates an in-memory workflow ledger with optional transition validation.
    /// </summary>
    public ScriptInMemoryWorkflowLedger(IScriptWorkflowLedgerTransitionValidator? transitionValidator)
    {
        _transitionValidator = transitionValidator ?? ScriptWorkflowLedgerSchemaValidator.Empty;
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> StartRunAsync(
        string workflowName,
        ScriptWorkflowRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedWorkflowName = RequireWorkflowName(workflowName);
        if (normalizedWorkflowName.Error != null)
        {
            return FailedRun(normalizedWorkflowName.Error);
        }

        lock (_gate)
        {
            var state = new RunState
            {
                Id = NewId(),
                WorkflowName = normalizedWorkflowName.Value!,
                Status = "running",
                Source = TrimToNull(options?.Source),
                StartedAt = DateTimeOffset.UtcNow,
                MetadataJson = options?.MetadataJson,
                Sequence = _sequence++
            };
            _runs[state.Id] = state;
            return SucceededRun(ToRun(state));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> GetRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRunId = RequireRunId(runId);
        if (normalizedRunId.Error != null)
        {
            return FailedRun(normalizedRunId.Error);
        }

        lock (_gate)
        {
            return _runs.TryGetValue(normalizedRunId.Value!, out var run)
                ? SucceededRun(ToRun(run))
                : FailedRun(Error(
                    ScriptWorkflowLedgerErrorCode.NotFound,
                    runId: normalizedRunId.Value!,
                    message: $"Workflow run '{normalizedRunId.Value}' was not found."));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowRun>>> ListRunsAsync(
        ScriptWorkflowRunQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        query ??= new ScriptWorkflowRunQuery();
        var workflowName = TrimToNull(query.WorkflowName);
        var status = TrimToNull(query.Status);
        lock (_gate)
        {
            var runs = _runs.Values
                .Where(run => workflowName == null || run.WorkflowName == workflowName)
                .Where(run => status == null || run.Status == status)
                .OrderByDescending(run => run.StartedAt)
                .ThenByDescending(run => run.Sequence)
                .Skip(Math.Max(0, query.Skip))
                .Take(ClampTake(query.Take))
                .Select(ToRun)
                .ToArray();

            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowRun>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkflowRun>(runs)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> FinishRunAsync(
        string runId,
        ScriptWorkflowRunFinishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UpdateRunStatus(runId, "completed", null, options?.MetadataJson);
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> FailRunAsync(
        string runId,
        ScriptWorkflowRunFailOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options == null)
        {
            return FailedRun(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Run failure options are required."));
        }

        var error = Require(options.Error, ScriptWorkflowLedgerErrorCode.InvalidRequest, "error", string.Empty, runId, string.Empty);
        if (error.Error != null)
        {
            return FailedRun(error.Error);
        }

        return UpdateRunStatus(runId, "failed", error.Value, options.MetadataJson);
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> CancelRunAsync(
        string runId,
        ScriptWorkflowRunCancelOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UpdateRunStatus(runId, "cancelled", TrimToNull(options?.Reason), options?.MetadataJson);
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> UpsertItemAsync(
        ScriptWorkflowItemUpsert input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input == null)
        {
            return FailedItem(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow item input is required."));
        }

        var workflowName = RequireWorkflowName(input.WorkflowName);
        if (workflowName.Error != null)
        {
            return FailedItem(workflowName.Error);
        }

        var itemKey = Require(input.ItemKey, ScriptWorkflowLedgerErrorCode.InvalidItemKey, "itemKey", workflowName.Value!, string.Empty, string.Empty);
        if (itemKey.Error != null)
        {
            return FailedItem(itemKey.Error);
        }

        var stage = Require(input.Stage, ScriptWorkflowLedgerErrorCode.InvalidRequest, "stage", workflowName.Value!, string.Empty, string.Empty);
        if (stage.Error != null)
        {
            return FailedItem(stage.Error);
        }

        var state = Require(input.State, ScriptWorkflowLedgerErrorCode.InvalidRequest, "state", workflowName.Value!, string.Empty, string.Empty);
        if (state.Error != null)
        {
            return FailedItem(state.Error);
        }

        lock (_gate)
        {
            var key = ItemKey(workflowName.Value!, itemKey.Value!);
            ItemState? current = null;
            if (_itemIdsByKey.TryGetValue(key, out var existingId))
            {
                current = _items[existingId];
                var transition = ValidateTransition(ToItem(current), stage.Value!, state.Value!, "upsert");
                if (!transition.Success)
                {
                    return FailedItem(transition.Error!);
                }
            }
            else
            {
                var creation = _transitionValidator.ValidateCreation(workflowName.Value!, itemKey.Value!, stage.Value!, state.Value!);
                if (!creation.Success)
                {
                    return FailedItem(creation.Error!);
                }
            }

            var now = DateTimeOffset.UtcNow;
            if (current == null)
            {
                current = new ItemState
                {
                    Id = NewId(),
                    WorkflowName = workflowName.Value!,
                    ItemKey = itemKey.Value!,
                    CreatedAt = now,
                    Sequence = _sequence++
                };
                _items[current.Id] = current;
                _itemIdsByKey[key] = current.Id;
            }

            current.ItemType = TrimToNull(input.ItemType);
            current.RunId = TrimToNull(input.RunId);
            current.Stage = stage.Value!;
            current.State = state.Value!;
            current.Priority = input.Priority;
            current.MaxAttempts = input.MaxAttempts;
            current.NextRetryAt = input.NextRetryAt;
            current.UpdatedAt = now;
            current.MetadataJson = input.MetadataJson;

            return SucceededItem(ToItem(current));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> GetItemAsync(
        string workflowName,
        string itemKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedWorkflowName = RequireWorkflowName(workflowName);
        if (normalizedWorkflowName.Error != null)
        {
            return FailedItem(normalizedWorkflowName.Error);
        }

        var normalizedItemKey = Require(itemKey, ScriptWorkflowLedgerErrorCode.InvalidItemKey, "itemKey", normalizedWorkflowName.Value!, string.Empty, string.Empty);
        if (normalizedItemKey.Error != null)
        {
            return FailedItem(normalizedItemKey.Error);
        }

        lock (_gate)
        {
            var key = ItemKey(normalizedWorkflowName.Value!, normalizedItemKey.Value!);
            if (_itemIdsByKey.TryGetValue(key, out var itemId) && _items.TryGetValue(itemId, out var item))
            {
                return SucceededItem(ToItem(item));
            }

            return FailedItem(Error(
                ScriptWorkflowLedgerErrorCode.NotFound,
                workflowName: normalizedWorkflowName.Value!,
                message: $"Workflow item '{normalizedWorkflowName.Value}/{normalizedItemKey.Value}' was not found."));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> GetItemByIdAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedItemId = RequireItemId(itemId);
        if (normalizedItemId.Error != null)
        {
            return FailedItem(normalizedItemId.Error);
        }

        lock (_gate)
        {
            return _items.TryGetValue(normalizedItemId.Value!, out var item)
                ? SucceededItem(ToItem(item))
                : FailedItem(Error(
                    ScriptWorkflowLedgerErrorCode.NotFound,
                    itemId: normalizedItemId.Value!,
                    message: $"Workflow item '{normalizedItemId.Value}' was not found."));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>> QueryItemsAsync(
        ScriptWorkflowItemQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow item query is required.")));
        }

        lock (_gate)
        {
            var items = QueryItemStates(query)
                .Select(ToItem)
                .ToArray();

            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkflowItem>(items)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> SetItemStateAsync(
        string itemId,
        ScriptWorkflowStateUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (update == null)
        {
            return FailedItem(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, itemId: itemId ?? string.Empty, message: "Workflow state update is required."));
        }

        var state = Require(update.State, ScriptWorkflowLedgerErrorCode.InvalidRequest, "state", string.Empty, string.Empty, itemId);
        if (state.Error != null)
        {
            return FailedItem(state.Error);
        }

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var item = current.Value!;
            var stage = TrimToNull(update.Stage) ?? item.Stage;
            var transition = ValidateTransition(ToItem(item), stage, state.Value!, "set state");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            item.Stage = stage;
            item.State = state.Value!;
            item.Priority = update.Priority ?? item.Priority;
            item.NextRetryAt = update.NextRetryAt;
            item.LeaseOwner = TrimToNull(update.LeaseOwner);
            item.LeaseExpiresAt = update.LeaseExpiresAt;
            item.LastError = update.LastError;
            item.LastErrorType = TrimToNull(update.LastErrorType);
            item.MetadataJson = update.MetadataJson;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowEvent>> AppendEventAsync(
        string itemId,
        ScriptWorkflowEventAppend input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input == null)
        {
            return FailedEvent(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, itemId: itemId ?? string.Empty, message: "Workflow event input is required."));
        }

        var eventType = Require(input.EventType, ScriptWorkflowLedgerErrorCode.InvalidEvent, "eventType", string.Empty, string.Empty, itemId);
        if (eventType.Error != null)
        {
            return FailedEvent(eventType.Error);
        }

        lock (_gate)
        {
            var item = GetRequiredItem(itemId);
            if (item.Error != null)
            {
                return FailedEvent(item.Error);
            }

            var itemState = item.Value!;
            var idempotencyKey = TrimToNull(input.IdempotencyKey);
            if (idempotencyKey != null)
            {
                var key = EventIdempotencyKey(itemState.Id, idempotencyKey);
                if (_eventIdsByIdempotencyKey.TryGetValue(key, out var existingId))
                {
                    return Task.FromResult(FailedEventIfMissingOrSucceeded(existingId));
                }
            }

            var evt = new EventState
            {
                Id = NewId(),
                RunId = TrimToNull(input.RunId) ?? itemState.RunId,
                ItemId = itemState.Id,
                EventType = eventType.Value!,
                Stage = TrimToNull(input.Stage),
                State = TrimToNull(input.State),
                Message = input.Message,
                Error = input.Error,
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow,
                MetadataJson = input.MetadataJson,
                Sequence = _sequence++
            };
            _events.Add(evt);
            if (idempotencyKey != null)
            {
                _eventIdsByIdempotencyKey[EventIdempotencyKey(itemState.Id, idempotencyKey)] = evt.Id;
            }

            return SucceededEvent(ToEvent(evt));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowEvent>>> GetEventsForItemAsync(
        string itemId,
        ScriptWorkflowEventQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedItemId = RequireItemId(itemId);
        if (normalizedItemId.Error != null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowEvent>>.Failed(normalizedItemId.Error));
        }

        query ??= new ScriptWorkflowEventQuery();
        lock (_gate)
        {
            var events = _events
                .Where(evt => evt.ItemId == normalizedItemId.Value)
                .OrderBy(evt => evt.CreatedAt)
                .ThenBy(evt => evt.Sequence)
                .Skip(Math.Max(0, query.Skip))
                .Take(ClampTake(query.Take))
                .Select(ToEvent)
                .ToArray();

            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowEvent>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkflowEvent>(events)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>> AttachArtifactAsync(
        string itemId,
        ScriptWorkflowArtifactAttach input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input == null)
        {
            return FailedArtifact(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, itemId: itemId ?? string.Empty, message: "Workflow artifact input is required."));
        }

        var artifactKind = Require(input.ArtifactKind, ScriptWorkflowLedgerErrorCode.InvalidArtifact, "artifactKind", string.Empty, string.Empty, itemId);
        if (artifactKind.Error != null)
        {
            return FailedArtifact(artifactKind.Error);
        }

        var artifactRef = Require(input.ArtifactRef, ScriptWorkflowLedgerErrorCode.InvalidArtifact, "artifactRef", string.Empty, string.Empty, itemId);
        if (artifactRef.Error != null)
        {
            return FailedArtifact(artifactRef.Error);
        }

        lock (_gate)
        {
            var item = GetRequiredItem(itemId);
            if (item.Error != null)
            {
                return FailedArtifact(item.Error);
            }

            var identity = ArtifactIdentity(item.Value!.Id, artifactKind.Value!, artifactRef.Value!);
            if (_artifactIdsByIdentity.TryGetValue(identity, out var existingId))
            {
                var existing = _artifacts.FirstOrDefault(artifact => artifact.Id == existingId);
                if (existing != null)
                {
                    return SucceededArtifact(ToArtifact(existing));
                }
            }

            var artifact = new ArtifactState
            {
                Id = NewId(),
                ItemId = item.Value.Id,
                EventId = TrimToNull(input.EventId),
                ArtifactKind = artifactKind.Value!,
                ArtifactRef = artifactRef.Value!,
                Role = TrimToNull(input.Role),
                CreatedAt = DateTimeOffset.UtcNow,
                MetadataJson = input.MetadataJson,
                Sequence = _sequence++
            };
            _artifacts.Add(artifact);
            _artifactIdsByIdentity[identity] = artifact.Id;
            return SucceededArtifact(ToArtifact(artifact));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowArtifact>>> GetArtifactsForItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedItemId = RequireItemId(itemId);
        if (normalizedItemId.Error != null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowArtifact>>.Failed(normalizedItemId.Error));
        }

        lock (_gate)
        {
            var artifacts = _artifacts
                .Where(artifact => artifact.ItemId == normalizedItemId.Value)
                .OrderBy(artifact => artifact.CreatedAt)
                .ThenBy(artifact => artifact.Sequence)
                .Select(ToArtifact)
                .ToArray();

            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowArtifact>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkflowArtifact>(artifacts)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> ClaimItemAsync(
        string itemId,
        ScriptWorkflowClaimOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateClaimOptions(options);
        if (validation.Error != null)
        {
            return FailedItem(validation.Error);
        }

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var transition = ValidateTransition(ToItem(current.Value!), current.Value!.Stage, "running", "claim");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            return SucceededItem(ToItem(ClaimCore(current.Value, validation.Value!.LeaseOwner, validation.Value.Now, validation.Value.LeaseDuration)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>> ClaimNextAsync(
        ScriptWorkflowItemQuery query,
        ScriptWorkflowClaimOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow item query is required.")));
        }

        var validation = ValidateClaimOptions(options);
        if (validation.Error != null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Failed(validation.Error));
        }

        if (validation.Value!.Take <= 0)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Succeeded(
                Array.Empty<ScriptWorkflowItem>()));
        }

        lock (_gate)
        {
            var candidates = SelectClaimCandidates(query, validation.Value.Now, validation.Value.Take).ToArray();
            foreach (var candidate in candidates)
            {
                var transition = ValidateTransition(ToItem(candidate), candidate.Stage, "running", "claim");
                if (!transition.Success)
                {
                    return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Failed(transition.Error!));
                }
            }

            var claimed = candidates
                .Select(candidate => ClaimCore(candidate, validation.Value.LeaseOwner, validation.Value.Now, validation.Value.LeaseDuration))
                .Select(ToItem)
                .ToArray();

            return Task.FromResult(ScriptWorkflowLedgerResult<IReadOnlyList<ScriptWorkflowItem>>.Succeeded(
                new ReadOnlyCollection<ScriptWorkflowItem>(claimed)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> CompleteItemAsync(
        string itemId,
        ScriptWorkflowItemCompleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var lease = EnsureLeaseOwnerIfProvided(current.Value!, options?.LeaseOwner);
            if (!lease.Success)
            {
                return FailedItem(lease.Error!);
            }

            var transition = ValidateTransition(ToItem(current.Value!), current.Value!.Stage, "completed", "complete");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            var item = current.Value!;
            item.State = "completed";
            item.LeaseOwner = null;
            item.LeaseExpiresAt = null;
            item.NextRetryAt = null;
            item.LastError = null;
            item.LastErrorType = null;
            item.MetadataJson = options?.MetadataJson ?? item.MetadataJson;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            InsertEvent(item.Id, item.RunId, "completed", item.Stage, item.State, null, null, null, options?.MetadataJson);
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> FailItemAsync(
        string itemId,
        ScriptWorkflowItemFailureOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options == null)
        {
            return FailedItem(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, itemId: itemId ?? string.Empty, message: "Workflow item failure options are required."));
        }

        var error = Require(options.Error, ScriptWorkflowLedgerErrorCode.InvalidRequest, "error", string.Empty, string.Empty, itemId);
        if (error.Error != null)
        {
            return FailedItem(error.Error);
        }

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var lease = EnsureLeaseOwnerIfProvided(current.Value!, options.LeaseOwner);
            if (!lease.Success)
            {
                return FailedItem(lease.Error!);
            }

            var item = current.Value!;
            var state = item.MaxAttempts.HasValue && item.AttemptCount >= item.MaxAttempts.Value
                ? "dead"
                : "retry_ready";
            var transition = ValidateTransition(ToItem(item), item.Stage, state, "fail");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            item.State = state;
            item.LeaseOwner = null;
            item.LeaseExpiresAt = null;
            item.NextRetryAt = state == "retry_ready"
                ? options.NextRetryAt ?? DateTimeOffset.UtcNow
                : null;
            item.LastError = error.Value;
            item.LastErrorType = TrimToNull(options.ErrorType);
            item.MetadataJson = options.MetadataJson ?? item.MetadataJson;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            InsertEvent(item.Id, item.RunId, "failed", item.Stage, item.State, null, error.Value, null, options.MetadataJson);
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> ReleaseItemAsync(
        string itemId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedLeaseOwner = Require(leaseOwner, ScriptWorkflowLedgerErrorCode.InvalidRequest, "leaseOwner", string.Empty, string.Empty, itemId);
        if (normalizedLeaseOwner.Error != null)
        {
            return FailedItem(normalizedLeaseOwner.Error);
        }

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var lease = EnsureLeaseOwnerIfProvided(current.Value!, normalizedLeaseOwner.Value);
            if (!lease.Success)
            {
                return FailedItem(lease.Error!);
            }

            var transition = ValidateTransition(ToItem(current.Value!), current.Value!.Stage, "pending", "release");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            var item = current.Value;
            item.State = "pending";
            item.LeaseOwner = null;
            item.LeaseExpiresAt = null;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            InsertEvent(item.Id, item.RunId, "released", item.Stage, item.State, null, null, null, null);
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> RetryItemAsync(
        string itemId,
        DateTimeOffset? nextRetryAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var state = nextRetryAt.HasValue ? "retry_ready" : "pending";
            var transition = ValidateTransition(ToItem(current.Value!), current.Value!.Stage, state, "retry");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            var item = current.Value;
            item.State = state;
            item.NextRetryAt = nextRetryAt;
            item.LeaseOwner = null;
            item.LeaseExpiresAt = null;
            item.LastError = null;
            item.LastErrorType = null;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            InsertEvent(item.Id, item.RunId, "retry_scheduled", item.Stage, item.State, null, null, null, null);
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> DeadLetterItemAsync(
        string itemId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedReason = Require(reason, ScriptWorkflowLedgerErrorCode.InvalidRequest, "reason", string.Empty, string.Empty, itemId);
        if (normalizedReason.Error != null)
        {
            return FailedItem(normalizedReason.Error);
        }

        lock (_gate)
        {
            var current = GetRequiredItem(itemId);
            if (current.Error != null)
            {
                return FailedItem(current.Error);
            }

            var transition = ValidateTransition(ToItem(current.Value!), current.Value!.Stage, "dead", "dead-letter");
            if (!transition.Success)
            {
                return FailedItem(transition.Error!);
            }

            var item = current.Value;
            item.State = "dead";
            item.LeaseOwner = null;
            item.LeaseExpiresAt = null;
            item.NextRetryAt = null;
            item.LastError = normalizedReason.Value;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            InsertEvent(item.Id, item.RunId, "dead_lettered", item.Stage, item.State, null, normalizedReason.Value, null, null);
            return SucceededItem(ToItem(item));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkRetryItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteBulkMutation(request, item => RetryItemCore(item, request.NextRetryAt));
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkCancelItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteBulkMutation(request, item => CancelItemCore(item, TrimToNull(request.Reason), request.MetadataJson));
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> BulkDeadLetterItemsAsync(
        ScriptWorkflowBulkMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reason = Require(request?.Reason, ScriptWorkflowLedgerErrorCode.InvalidRequest, "reason", string.Empty, string.Empty, string.Empty);
        if (reason.Error != null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>.Failed(reason.Error));
        }

        return ExecuteBulkMutation(request!, item => DeadLetterItemCore(item, reason.Value!));
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>> PreviewRetentionAsync(
        ScriptWorkflowLedgerRetentionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteRetention(options, dryRun: true);
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>> PruneRetentionAsync(
        ScriptWorkflowLedgerRetentionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteRetention(options, dryRun: false);
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerExport>> ExportHistoryAsync(
        ScriptWorkflowLedgerExportOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerExport>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow ledger export options are required.")));
        }

        lock (_gate)
        {
            var take = ClampTake(options.Take);
            var workflowName = TrimToNull(options.WorkflowName);
            var runId = TrimToNull(options.RunId);
            var itemIds = options.ItemIds?
                .Select(TrimToNull)
                .Where(id => id != null)
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .Take(take)
                .ToArray();

            var items = new List<ItemState>();
            if (itemIds is { Length: > 0 })
            {
                foreach (var itemId in itemIds)
                {
                    if (_items.TryGetValue(itemId, out var item))
                    {
                        items.Add(item);
                    }
                }
            }
            else if (runId != null)
            {
                items.AddRange(QueryItemStates(new ScriptWorkflowItemQuery { RunId = runId, Take = take }));
            }
            else
            {
                items.AddRange(QueryItemStates(new ScriptWorkflowItemQuery { WorkflowName = workflowName, Take = take }));
            }

            var runs = new Dictionary<string, RunState>(StringComparer.Ordinal);
            if (itemIds is not { Length: > 0 })
            {
                if (runId != null)
                {
                    if (_runs.TryGetValue(runId, out var run))
                    {
                        runs[run.Id] = run;
                    }
                }
                else
                {
                    foreach (var run in _runs.Values
                        .Where(run => workflowName == null || run.WorkflowName == workflowName)
                        .OrderByDescending(run => run.StartedAt)
                        .ThenByDescending(run => run.Sequence)
                        .Take(take))
                    {
                        runs[run.Id] = run;
                    }
                }
            }

            foreach (var referencedRunId in items.Select(item => item.RunId).Where(id => id != null).Distinct(StringComparer.Ordinal))
            {
                if (_runs.TryGetValue(referencedRunId!, out var run))
                {
                    runs[run.Id] = run;
                }
            }

            var itemIdSet = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var events = options.IncludeEvents
                ? _events
                    .Where(evt => evt.ItemId != null && itemIdSet.Contains(evt.ItemId))
                    .OrderBy(evt => evt.CreatedAt)
                    .ThenBy(evt => evt.Sequence)
                    .Select(ToEvent)
                    .ToArray()
                : Array.Empty<ScriptWorkflowEvent>();
            var artifacts = options.IncludeArtifacts
                ? _artifacts
                    .Where(artifact => itemIdSet.Contains(artifact.ItemId))
                    .OrderBy(artifact => artifact.CreatedAt)
                    .ThenBy(artifact => artifact.Sequence)
                    .Select(ToArtifact)
                    .ToArray()
                : Array.Empty<ScriptWorkflowArtifact>();

            var export = new ScriptWorkflowLedgerExport
            {
                SchemaVersion = 1,
                ExportedAt = DateTimeOffset.UtcNow,
                Runs = runs.Values
                    .OrderBy(run => run.StartedAt)
                    .ThenBy(run => run.Id, StringComparer.Ordinal)
                    .Select(ToRun)
                    .ToArray(),
                Items = items
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(ToItem)
                    .ToArray(),
                Events = events,
                Artifacts = artifacts
            };

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerExport>.Succeeded(export));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>> ImportHistoryAsync(
        ScriptWorkflowLedgerExport export,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (export == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow ledger export is required.")));
        }

        var validation = ValidateImport(export);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>.Failed(validation.Error!));
        }

        lock (_gate)
        {
            var referenceValidation = ValidateImportReferences(export);
            if (!referenceValidation.Success)
            {
                return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>.Failed(referenceValidation.Error!));
            }

            var runsInserted = 0;
            var runsSkipped = 0;
            var itemsInserted = 0;
            var itemsSkipped = 0;
            var eventsInserted = 0;
            var eventsSkipped = 0;
            var artifactsInserted = 0;
            var artifactsSkipped = 0;

            foreach (var run in export.Runs)
            {
                if (_runs.ContainsKey(run.Id))
                {
                    runsSkipped++;
                    continue;
                }

                _runs[run.Id] = FromRun(run);
                runsInserted++;
            }

            foreach (var item in export.Items)
            {
                if (_items.ContainsKey(item.Id))
                {
                    itemsSkipped++;
                    continue;
                }

                var key = ItemKey(item.WorkflowName, item.ItemKey);
                if (_itemIdsByKey.ContainsKey(key))
                {
                    return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>.Failed(
                        Error(
                            ScriptWorkflowLedgerErrorCode.InvalidRequest,
                            workflowName: item.WorkflowName,
                            itemId: item.Id,
                            message: $"Workflow item key '{item.WorkflowName}/{item.ItemKey}' already exists.")));
                }

                _items[item.Id] = FromItem(item);
                _itemIdsByKey[key] = item.Id;
                itemsInserted++;
            }

            foreach (var evt in export.Events)
            {
                if (_events.Any(existing => existing.Id == evt.Id))
                {
                    eventsSkipped++;
                    continue;
                }

                var state = FromEvent(evt);
                _events.Add(state);
                if (state.ItemId != null && state.IdempotencyKey != null)
                {
                    _eventIdsByIdempotencyKey[EventIdempotencyKey(state.ItemId, state.IdempotencyKey)] = state.Id;
                }

                eventsInserted++;
            }

            foreach (var artifact in export.Artifacts)
            {
                if (_artifacts.Any(existing => existing.Id == artifact.Id))
                {
                    artifactsSkipped++;
                    continue;
                }

                var state = FromArtifact(artifact);
                _artifacts.Add(state);
                _artifactIdsByIdentity[ArtifactIdentity(state.ItemId, state.ArtifactKind, state.ArtifactRef)] = state.Id;
                artifactsInserted++;
            }

            var result = new ScriptWorkflowLedgerImportResult(
                runsInserted,
                runsSkipped,
                itemsInserted,
                itemsSkipped,
                eventsInserted,
                eventsSkipped,
                artifactsInserted,
                artifactsSkipped,
                0,
                Array.Empty<string>());
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerImportResult>.Succeeded(result));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerOverview>> GetOverviewAsync(
        ScriptWorkflowLedgerOverviewQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerOverview>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow ledger overview query is required.")));
        }

        lock (_gate)
        {
            var now = query.NowUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
            var runs = FilterRuns(query).ToArray();
            var items = FilterItems(query).ToArray();
            var overview = new ScriptWorkflowLedgerOverview(
                runs.Length,
                items.Length,
                CountBy(runs, run => run.Status),
                CountBy(items, item => item.State),
                CountBy(items, item => item.Stage),
                items.Count(item => item.State == "retry_ready" && (!item.NextRetryAt.HasValue || item.NextRetryAt <= now)),
                items.Count(item => item.LeaseOwner != null && item.LeaseExpiresAt.HasValue && item.LeaseExpiresAt > now),
                items.Count(item => item.LeaseOwner != null && item.LeaseExpiresAt.HasValue && item.LeaseExpiresAt <= now),
                BuildAttentionItems(runs, items, now, ClampTake(query.AttentionTake)));

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerOverview>.Succeeded(overview));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowTransitionGraph>> GetTransitionGraphAsync(
        ScriptWorkflowTransitionGraphQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowTransitionGraph>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow transition graph query is required.")));
        }

        lock (_gate)
        {
            var take = ClampTake(query.Take);
            if (take <= 0)
            {
                return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowTransitionGraph>.Succeeded(
                    EmptyTransitionGraph()));
            }

            var workflowName = TrimToNull(query.WorkflowName);
            var runId = TrimToNull(query.RunId);
            var graphItems = _items.Values
                .Where(item => workflowName == null || item.WorkflowName == workflowName)
                .Where(item => runId == null || item.RunId == runId)
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Sequence)
                .Take(take)
                .ToArray();
            var itemIds = graphItems.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var nodeBuilders = new Dictionary<string, TransitionNodeBuilder>(StringComparer.Ordinal);
            var edgeBuilders = new Dictionary<string, TransitionEdgeBuilder>(StringComparer.Ordinal);

            foreach (var item in graphItems)
            {
                var node = GetOrAddTransitionNode(nodeBuilders, item.Stage, item.State);
                node.CurrentItemCount++;
                node.MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Observed);
            }

            foreach (var itemEvents in _events
                .Where(evt => evt.ItemId != null && itemIds.Contains(evt.ItemId))
                .Where(evt => evt.Stage != null && evt.State != null)
                .OrderBy(evt => evt.ItemId, StringComparer.Ordinal)
                .ThenBy(evt => evt.CreatedAt)
                .ThenBy(evt => evt.Sequence)
                .GroupBy(evt => evt.ItemId!, StringComparer.Ordinal))
            {
                EventState? previous = null;
                foreach (var evt in itemEvents)
                {
                    var node = GetOrAddTransitionNode(nodeBuilders, evt.Stage!, evt.State!);
                    node.EventCount++;
                    node.MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Observed);
                    if (IsProblemTransitionEvent(evt.EventType, evt.Error))
                    {
                        node.ErrorCount++;
                    }

                    if (previous != null &&
                        (!string.Equals(previous.Stage, evt.Stage, StringComparison.Ordinal) ||
                            !string.Equals(previous.State, evt.State, StringComparison.Ordinal)))
                    {
                        var edgeKey = TransitionEdgeKey(previous.Stage!, previous.State!, evt.Stage!, evt.State!);
                        if (!edgeBuilders.TryGetValue(edgeKey, out var edge))
                        {
                            edge = new TransitionEdgeBuilder(previous.Stage!, previous.State!, evt.Stage!, evt.State!);
                            edgeBuilders[edgeKey] = edge;
                        }

                        edge.Count++;
                        edge.SampleItemId ??= evt.ItemId;
                        if (!edge.LastSeenAt.HasValue || evt.CreatedAt > edge.LastSeenAt.Value)
                        {
                            edge.LastSeenAt = evt.CreatedAt;
                        }

                        if (IsProblemTransitionEvent(evt.EventType, evt.Error))
                        {
                            edge.ErrorCount++;
                        }

                        if (IsRetryTransitionEvent(evt.EventType))
                        {
                            edge.RetryCount++;
                        }

                        if (IsDeadLetterTransitionEvent(evt.EventType))
                        {
                            edge.DeadLetterCount++;
                        }
                    }

                    previous = evt;
                }
            }

            var schemaOverlay = ApplyTransitionGraphSchemaOverlay(
                ResolveTransitionGraphWorkflowName(workflowName, runId),
                nodeBuilders,
                edgeBuilders);

            var nodes = nodeBuilders.Values
                .OrderBy(node => node.Stage, StringComparer.Ordinal)
                .ThenBy(node => node.State, StringComparer.Ordinal)
                .Select(node => new ScriptWorkflowTransitionNode(
                    TransitionNodeId(node.Stage, node.State),
                    node.Stage,
                    node.State,
                    node.CurrentItemCount,
                    node.EventCount,
                    node.ErrorCount,
                    node.SchemaStatus))
                .ToArray();
            var edges = edgeBuilders.Values
                .OrderByDescending(edge => edge.Count)
                .ThenBy(edge => edge.SchemaStatus == ScriptWorkflowTransitionSchemaStatus.SchemaOnly ? 1 : 0)
                .ThenBy(edge => edge.FromStage, StringComparer.Ordinal)
                .ThenBy(edge => edge.ToStage, StringComparer.Ordinal)
                .Select(edge => new ScriptWorkflowTransitionEdge(
                    TransitionNodeId(edge.FromStage, edge.FromState),
                    TransitionNodeId(edge.ToStage, edge.ToState),
                    edge.FromStage,
                    edge.FromState,
                    edge.ToStage,
                    edge.ToState,
                    edge.Count,
                    edge.ErrorCount,
                    edge.RetryCount,
                    edge.DeadLetterCount,
                    edge.LastSeenAt,
                    edge.SampleItemId,
                    edge.SchemaStatus))
                .ToArray();
            var graph = new ScriptWorkflowTransitionGraph(
                graphItems.Length,
                edges.Sum(edge => edge.Count),
                nodes,
                edges,
                schemaOverlay.Status,
                schemaOverlay.Message,
                schemaOverlay.AllowedTransitionCount,
                schemaOverlay.UnexpectedTransitionCount,
                schemaOverlay.SchemaOnlyTransitionCount);

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowTransitionGraph>.Succeeded(graph));
        }
    }

    /// <inheritdoc />
    public Task<ScriptWorkflowLedgerResult<ScriptWorkflowFlowEvidence>> GetFlowEvidenceAsync(
        ScriptWorkflowFlowEvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (query == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowFlowEvidence>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow flow evidence query is required.")));
        }

        lock (_gate)
        {
            var workflowName = TrimToNull(query.WorkflowName);
            var runId = TrimToNull(query.RunId);
            var take = ClampTake(query.Take);
            if (take <= 0)
            {
                return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowFlowEvidence>.Succeeded(
                    new ScriptWorkflowFlowEvidence(
                        workflowName,
                        runId,
                        0,
                        new Dictionary<string, int>(StringComparer.Ordinal),
                        new Dictionary<string, ScriptWorkflowFlowQueueEvidence>(StringComparer.OrdinalIgnoreCase))));
            }

            var items = _items.Values
                .Where(item => workflowName == null || item.WorkflowName == workflowName)
                .Where(item => runId == null || item.RunId == runId)
                .ToArray();
            var itemIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var itemCountsByState = CountBy(items, item => item.State);
            var queueBuilders = new Dictionary<string, FlowQueueEvidenceBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in _events
                .Where(evt => evt.ItemId != null && itemIds.Contains(evt.ItemId))
                .Where(evt => evt.MetadataJson != null)
                .OrderByDescending(evt => evt.CreatedAt)
                .ThenByDescending(evt => evt.Sequence)
                .Take(take))
            {
                var queueName = TryReadQueueName(evt.MetadataJson!);
                if (queueName == null || !_items.TryGetValue(evt.ItemId!, out var item))
                {
                    continue;
                }

                if (!queueBuilders.TryGetValue(queueName, out var queue))
                {
                    queue = new FlowQueueEvidenceBuilder(queueName);
                    queueBuilders[queueName] = queue;
                }

                queue.Add(item, evt);
            }

            var queues = queueBuilders
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToEvidence(), StringComparer.OrdinalIgnoreCase);
            var evidence = new ScriptWorkflowFlowEvidence(
                workflowName,
                runId,
                items.Length,
                itemCountsByState,
                queues);

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowFlowEvidence>.Succeeded(evidence));
        }
    }

    private Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> UpdateRunStatus(
        string runId,
        string status,
        string? lastError,
        string? metadataJson)
    {
        var normalizedRunId = RequireRunId(runId);
        if (normalizedRunId.Error != null)
        {
            return FailedRun(normalizedRunId.Error);
        }

        lock (_gate)
        {
            if (!_runs.TryGetValue(normalizedRunId.Value!, out var run))
            {
                return FailedRun(Error(
                    ScriptWorkflowLedgerErrorCode.NotFound,
                    runId: normalizedRunId.Value!,
                    message: $"Workflow run '{normalizedRunId.Value}' was not found."));
            }

            run.Status = status;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.LastError = lastError;
            run.MetadataJson = metadataJson;
            return SucceededRun(ToRun(run));
        }
    }

    private Task<ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>> ExecuteBulkMutation(
        ScriptWorkflowBulkMutationRequest request,
        Func<ItemState, ScriptWorkflowLedgerResult<ScriptWorkflowItem>> mutate)
    {
        if (request == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow bulk mutation request is required.")));
        }

        lock (_gate)
        {
            var targets = ResolveBulkTargets(request);
            if (targets.Error != null)
            {
                return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>.Failed(targets.Error));
            }

            var items = new List<ScriptWorkflowItem>();
            var errors = targets.Value!.Errors.ToList();
            foreach (var item in targets.Value.Items)
            {
                var result = mutate(item);
                if (result.Success)
                {
                    items.Add(result.Value!);
                }
                else
                {
                    errors.Add(new ScriptWorkflowBulkMutationError(
                        item.Id,
                        item.ItemKey,
                        item.WorkflowName,
                        result.Error!.Message));
                }
            }

            var value = new ScriptWorkflowBulkMutationResult(
                targets.Value.Requested,
                targets.Value.Items.Count,
                items.Count,
                errors.Count,
                new ReadOnlyCollection<ScriptWorkflowItem>(items),
                new ReadOnlyCollection<ScriptWorkflowBulkMutationError>(errors));

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowBulkMutationResult>.Succeeded(value));
        }
    }

    private ValueOrError<BulkTargets> ResolveBulkTargets(ScriptWorkflowBulkMutationRequest request)
    {
        var itemIds = request.ItemIds?
            .Select(TrimToNull)
            .Where(id => id != null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var hasItemIds = itemIds is { Length: > 0 };
        var hasQuery = request.Query != null;
        if (hasItemIds == hasQuery)
        {
            return WithError<BulkTargets>(Error(
                ScriptWorkflowLedgerErrorCode.InvalidRequest,
                message: "Specify either item ids or a query, but not both."));
        }

        var maxItems = ClampTake(request.MaxItems);
        if (maxItems <= 0)
        {
            return WithError<BulkTargets>(Error(
                ScriptWorkflowLedgerErrorCode.InvalidRequest,
                message: "MaxItems must be positive."));
        }

        var items = new List<ItemState>();
        var errors = new List<ScriptWorkflowBulkMutationError>();
        var requested = 0;
        if (hasItemIds)
        {
            requested = itemIds!.Length;
            foreach (var itemId in itemIds.Take(maxItems))
            {
                if (_items.TryGetValue(itemId, out var item))
                {
                    items.Add(item);
                }
                else
                {
                    errors.Add(new ScriptWorkflowBulkMutationError(
                        itemId,
                        null,
                        null,
                        $"Workflow item '{itemId}' was not found."));
                }
            }
        }
        else
        {
            var take = request.Query!.Take > 0 ? Math.Min(ClampTake(request.Query.Take), maxItems) : maxItems;
            items.AddRange(QueryItemStates(request.Query with { Take = take }));
            requested = items.Count;
        }

        return new ValueOrError<BulkTargets>(new BulkTargets(requested, items, errors), null);
    }

    private Task<ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>> ExecuteRetention(
        ScriptWorkflowLedgerRetentionOptions options,
        bool dryRun)
    {
        if (options == null)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow ledger retention options are required.")));
        }

        if (!options.OlderThanUtc.HasValue)
        {
            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>.Failed(
                Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "OlderThanUtc is required.")));
        }

        var workflowName = TrimToNull(options.WorkflowName);
        var cutoff = options.OlderThanUtc.Value.ToUniversalTime();
        lock (_gate)
        {
            var runIds = _runs.Values
                .Where(run => workflowName == null || run.WorkflowName == workflowName)
                .Where(run => IsTerminalRunStatus(run.Status))
                .Where(run => run.FinishedAt.HasValue && run.FinishedAt.Value < cutoff)
                .Select(run => run.Id)
                .ToHashSet(StringComparer.Ordinal);
            var itemIds = _items.Values
                .Where(item => runIds.Contains(item.RunId ?? string.Empty))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (options.IncludeStandaloneTerminalItems)
            {
                foreach (var item in _items.Values
                    .Where(item => workflowName == null || item.WorkflowName == workflowName)
                    .Where(item => item.RunId == null)
                    .Where(item => IsTerminalItemState(item.State))
                    .Where(item => item.UpdatedAt < cutoff))
                {
                    itemIds.Add(item.Id);
                }
            }

            var eventsDeleted = _events.Count(evt =>
                (evt.ItemId != null && itemIds.Contains(evt.ItemId)) ||
                (evt.ItemId == null && evt.RunId != null && runIds.Contains(evt.RunId)));
            var artifactsDeleted = _artifacts.Count(artifact => itemIds.Contains(artifact.ItemId));
            var result = new ScriptWorkflowLedgerRetentionResult(
                runIds.Count,
                itemIds.Count,
                eventsDeleted,
                artifactsDeleted,
                !dryRun && options.Vacuum && (runIds.Count > 0 || itemIds.Count > 0 || eventsDeleted > 0 || artifactsDeleted > 0),
                dryRun);

            if (!dryRun)
            {
                foreach (var itemId in itemIds)
                {
                    if (_items.Remove(itemId, out var item))
                    {
                        _itemIdsByKey.Remove(ItemKey(item.WorkflowName, item.ItemKey));
                    }
                }

                foreach (var runId in runIds)
                {
                    _runs.Remove(runId);
                }

                _events.RemoveAll(evt =>
                    (evt.ItemId != null && itemIds.Contains(evt.ItemId)) ||
                    (evt.ItemId == null && evt.RunId != null && runIds.Contains(evt.RunId)));
                _artifacts.RemoveAll(artifact => itemIds.Contains(artifact.ItemId));
                RebuildEventIdempotencyIndex();
                RebuildArtifactIdentityIndex();
            }

            return Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowLedgerRetentionResult>.Succeeded(result));
        }
    }

    private IEnumerable<RunState> FilterRuns(ScriptWorkflowLedgerOverviewQuery query)
    {
        var workflowName = TrimToNull(query.WorkflowName);
        var runId = TrimToNull(query.RunId);
        var itemId = TrimToNull(query.ItemId);
        if (itemId != null)
        {
            if (!_items.TryGetValue(itemId, out var item) || item.RunId == null || !_runs.TryGetValue(item.RunId, out var run))
            {
                return Array.Empty<RunState>();
            }

            return WorkflowRunMatches(run, workflowName, runId) ? [run] : Array.Empty<RunState>();
        }

        return _runs.Values.Where(run => WorkflowRunMatches(run, workflowName, runId));
    }

    private IEnumerable<ItemState> FilterItems(ScriptWorkflowLedgerOverviewQuery query)
    {
        var workflowName = TrimToNull(query.WorkflowName);
        var runId = TrimToNull(query.RunId);
        var itemId = TrimToNull(query.ItemId);
        return _items.Values
            .Where(item => itemId == null || item.Id == itemId)
            .Where(item => workflowName == null || item.WorkflowName == workflowName)
            .Where(item => runId == null || item.RunId == runId);
    }

    private static bool WorkflowRunMatches(RunState run, string? workflowName, string? runId) =>
        (workflowName == null || run.WorkflowName == workflowName) &&
        (runId == null || run.Id == runId);

    private static IReadOnlyDictionary<string, int> CountBy<T>(
        IEnumerable<T> source,
        Func<T, string> keySelector) =>
        source
            .GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static IReadOnlyList<ScriptWorkflowLedgerAttentionItem> BuildAttentionItems(
        IReadOnlyList<RunState> runs,
        IReadOnlyList<ItemState> items,
        DateTimeOffset now,
        int take)
    {
        if (take <= 0)
        {
            return Array.Empty<ScriptWorkflowLedgerAttentionItem>();
        }

        var attention = new List<PrioritizedAttentionItem>();
        AddItemAttentionItems(attention, items, "expired_lease", "critical", 0, item =>
            item.LeaseOwner != null && item.LeaseExpiresAt.HasValue && item.LeaseExpiresAt <= now,
            "Workflow item lease has expired.");
        AddItemAttentionItems(attention, items, "dead_item", "error", 1, item =>
            item.State == "dead",
            "Workflow item is dead-lettered.");
        AddItemAttentionItems(attention, items, "failed_item", "error", 2, item =>
            item.State == "failed",
            "Workflow item failed.");
        AddItemAttentionItems(attention, items, "retry_ready", "warning", 3, item =>
            item.State == "retry_ready" && (!item.NextRetryAt.HasValue || item.NextRetryAt <= now),
            "Workflow item is ready to retry.");

        foreach (var run in runs.Where(run => run.Status == "failed"))
        {
            attention.Add(new PrioritizedAttentionItem(
                4,
                new ScriptWorkflowLedgerAttentionItem(
                    "failed_run",
                    "error",
                    run.WorkflowName,
                    run.Id,
                    null,
                    null,
                    null,
                    null,
                    "Workflow run failed.",
                    run.FinishedAt ?? run.StartedAt,
                    run.LastError)));
        }

        return attention
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.Item.Timestamp)
            .Take(take)
            .Select(item => item.Item)
            .ToArray();
    }

    private static void AddItemAttentionItems(
        List<PrioritizedAttentionItem> attention,
        IEnumerable<ItemState> items,
        string kind,
        string severity,
        int priority,
        Func<ItemState, bool> predicate,
        string message)
    {
        foreach (var item in items.Where(predicate))
        {
            attention.Add(new PrioritizedAttentionItem(
                priority,
                new ScriptWorkflowLedgerAttentionItem(
                    kind,
                    severity,
                    item.WorkflowName,
                    item.RunId,
                    item.Id,
                    item.ItemKey,
                    item.Stage,
                    item.State,
                    message,
                    item.UpdatedAt,
                    item.LastError)));
        }
    }

    private static ScriptWorkflowTransitionGraph EmptyTransitionGraph() =>
        new(
            0,
            0,
            Array.Empty<ScriptWorkflowTransitionNode>(),
            Array.Empty<ScriptWorkflowTransitionEdge>());

    private string? ResolveTransitionGraphWorkflowName(string? workflowName, string? runId)
    {
        if (workflowName != null)
        {
            return workflowName;
        }

        return runId == null || !_runs.TryGetValue(runId, out var run) ? null : run.WorkflowName;
    }

    private TransitionSchemaOverlay ApplyTransitionGraphSchemaOverlay(
        string? workflowName,
        Dictionary<string, TransitionNodeBuilder> nodes,
        Dictionary<string, TransitionEdgeBuilder> edges)
    {
        if (workflowName == null)
        {
            return new TransitionSchemaOverlay(
                ScriptWorkflowTransitionSchemaStatus.NoWorkflowSelected,
                "Select one workflow to compare observed transitions with a workflow schema.");
        }

        if (_transitionValidator is not IScriptWorkflowLedgerSchemaProvider schemaProvider)
        {
            return new TransitionSchemaOverlay(
                ScriptWorkflowTransitionSchemaStatus.NoSchema,
                $"Workflow '{workflowName}' has no ledger schema.");
        }

        if (schemaProvider.TryGetInvalidSchemaError(workflowName, out var error))
        {
            return new TransitionSchemaOverlay(
                ScriptWorkflowTransitionSchemaStatus.InvalidSchema,
                $"Workflow '{workflowName}' schema is invalid: {error}");
        }

        if (!schemaProvider.TryGetSchema(workflowName, out var schema))
        {
            return new TransitionSchemaOverlay(
                ScriptWorkflowTransitionSchemaStatus.NoSchema,
                $"Workflow '{workflowName}' has no ledger schema.");
        }

        var allowedTransitionCount = 0;
        var unexpectedTransitionCount = 0;
        foreach (var edge in edges.Values)
        {
            if (schema.AllowsTransition(edge.FromStage, edge.FromState, edge.ToStage, edge.ToState))
            {
                edge.SchemaStatus = ScriptWorkflowTransitionSchemaStatus.Allowed;
                allowedTransitionCount += edge.Count;
                GetOrAddTransitionNode(nodes, edge.FromStage, edge.FromState).MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Allowed);
                GetOrAddTransitionNode(nodes, edge.ToStage, edge.ToState).MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Allowed);
            }
            else
            {
                edge.SchemaStatus = ScriptWorkflowTransitionSchemaStatus.Unexpected;
                unexpectedTransitionCount += edge.Count;
                GetOrAddTransitionNode(nodes, edge.FromStage, edge.FromState).MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Unexpected);
                GetOrAddTransitionNode(nodes, edge.ToStage, edge.ToState).MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.Unexpected);
            }
        }

        var schemaOnlyTransitionCount = 0;
        foreach (var transition in schema.Transitions.Where(transition => transition.IsConcrete))
        {
            var edgeKey = TransitionEdgeKey(transition.FromStage!, transition.FromState!, transition.ToStage!, transition.ToState!);
            if (edges.ContainsKey(edgeKey))
            {
                continue;
            }

            schemaOnlyTransitionCount++;
            GetOrAddTransitionNode(nodes, transition.FromStage!, transition.FromState!)
                .MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.SchemaOnly);
            GetOrAddTransitionNode(nodes, transition.ToStage!, transition.ToState!)
                .MarkSchemaStatus(ScriptWorkflowTransitionSchemaStatus.SchemaOnly);
            edges[edgeKey] = new TransitionEdgeBuilder(
                transition.FromStage!,
                transition.FromState!,
                transition.ToStage!,
                transition.ToState!)
            {
                SchemaStatus = ScriptWorkflowTransitionSchemaStatus.SchemaOnly
            };
        }

        return new TransitionSchemaOverlay(
            ScriptWorkflowTransitionSchemaStatus.ActiveSchema,
            unexpectedTransitionCount > 0
                ? $"{unexpectedTransitionCount} observed transition(s) do not match the workflow schema."
                : "Observed transitions match the workflow schema.",
            allowedTransitionCount,
            unexpectedTransitionCount,
            schemaOnlyTransitionCount);
    }

    private static TransitionNodeBuilder GetOrAddTransitionNode(
        Dictionary<string, TransitionNodeBuilder> nodes,
        string stage,
        string state)
    {
        var id = TransitionNodeId(stage, state);
        if (!nodes.TryGetValue(id, out var node))
        {
            node = new TransitionNodeBuilder(stage, state);
            nodes[id] = node;
        }

        return node;
    }

    private static string TransitionEdgeKey(string fromStage, string fromState, string toStage, string toState) =>
        $"{TransitionNodeId(fromStage, fromState)}\u001f{TransitionNodeId(toStage, toState)}";

    private static string TransitionNodeId(string stage, string state) => $"{stage}\u001f{state}";

    private static bool IsProblemTransitionEvent(string eventType, string? error) =>
        !string.IsNullOrWhiteSpace(error) ||
        eventType.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("dead", StringComparison.OrdinalIgnoreCase) ||
        eventType.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetryTransitionEvent(string eventType) =>
        eventType.Contains("retry", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeadLetterTransitionEvent(string eventType) =>
        eventType.Contains("dead", StringComparison.OrdinalIgnoreCase);

    private static string? TryReadQueueName(string metadataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("queueName", out var queueNameElement) ||
                queueNameElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return TrimToNull(queueNameElement.GetString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsProblemFlowEvidenceItem(ItemState item) =>
        !string.IsNullOrWhiteSpace(item.LastError) ||
        item.State is "dead" or "failed" or "cancelled" or "canceled";

    private ScriptWorkflowLedgerResult ValidateImport(ScriptWorkflowLedgerExport export)
    {
        if (export.SchemaVersion != 1)
        {
            return ScriptWorkflowLedgerResult.Failed(Error(
                ScriptWorkflowLedgerErrorCode.InvalidRequest,
                message: "Workflow ledger export schema version is not supported."));
        }

        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in export.Runs)
        {
            var required = ValidateRequiredImport(run.Id, "run.id") ??
                ValidateRequiredImport(run.WorkflowName, "run.workflowName") ??
                ValidateRequiredImport(run.Status, "run.status");
            if (required != null)
            {
                return required;
            }

            if (run.StartedAt == default)
            {
                return InvalidImport("run.startedAt is required.");
            }

            if (!runIds.Add(run.Id))
            {
                return InvalidImport($"Duplicate workflow run ID '{run.Id}' in import.");
            }
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var itemKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in export.Items)
        {
            var required = ValidateRequiredImport(item.Id, "item.id") ??
                ValidateRequiredImport(item.WorkflowName, "item.workflowName") ??
                ValidateRequiredImport(item.ItemKey, "item.itemKey") ??
                ValidateRequiredImport(item.Stage, "item.stage") ??
                ValidateRequiredImport(item.State, "item.state");
            if (required != null)
            {
                return required;
            }

            if (item.CreatedAt == default)
            {
                return InvalidImport("item.createdAt is required.");
            }

            if (item.UpdatedAt == default)
            {
                return InvalidImport("item.updatedAt is required.");
            }

            if (!itemIds.Add(item.Id))
            {
                return InvalidImport($"Duplicate workflow item ID '{item.Id}' in import.");
            }

            if (!itemKeys.Add(ItemKey(item.WorkflowName, item.ItemKey)))
            {
                return InvalidImport($"Duplicate workflow item key '{item.WorkflowName}/{item.ItemKey}' in import.");
            }
        }

        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evt in export.Events)
        {
            var required = ValidateRequiredImport(evt.Id, "event.id") ??
                ValidateRequiredImport(evt.EventType, "event.eventType");
            if (required != null)
            {
                return required;
            }

            if (evt.CreatedAt == default)
            {
                return InvalidImport("event.createdAt is required.");
            }

            if (!eventIds.Add(evt.Id))
            {
                return InvalidImport($"Duplicate workflow event ID '{evt.Id}' in import.");
            }
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in export.Artifacts)
        {
            var required = ValidateRequiredImport(artifact.Id, "artifact.id") ??
                ValidateRequiredImport(artifact.ItemId, "artifact.itemId") ??
                ValidateRequiredImport(artifact.ArtifactKind, "artifact.artifactKind") ??
                ValidateRequiredImport(artifact.ArtifactRef, "artifact.artifactRef");
            if (required != null)
            {
                return required;
            }

            if (artifact.CreatedAt == default)
            {
                return InvalidImport("artifact.createdAt is required.");
            }

            if (!artifactIds.Add(artifact.Id))
            {
                return InvalidImport($"Duplicate workflow artifact ID '{artifact.Id}' in import.");
            }
        }

        return ScriptWorkflowLedgerResult.Succeeded();
    }

    private ScriptWorkflowLedgerResult ValidateImportReferences(ScriptWorkflowLedgerExport export)
    {
        var runIds = export.Runs.Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        var itemIds = export.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var eventIds = export.Events.Select(evt => evt.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var item in export.Items)
        {
            if (item.RunId != null && !runIds.Contains(item.RunId) && !_runs.ContainsKey(item.RunId))
            {
                return InvalidImport($"Workflow item '{item.Id}' references missing run '{item.RunId}'.");
            }
        }

        foreach (var evt in export.Events)
        {
            if (evt.ItemId != null && !itemIds.Contains(evt.ItemId) && !_items.ContainsKey(evt.ItemId))
            {
                return InvalidImport($"Workflow event '{evt.Id}' references missing item '{evt.ItemId}'.");
            }

            if (evt.RunId != null && !runIds.Contains(evt.RunId) && !_runs.ContainsKey(evt.RunId))
            {
                return InvalidImport($"Workflow event '{evt.Id}' references missing run '{evt.RunId}'.");
            }
        }

        foreach (var artifact in export.Artifacts)
        {
            if (!itemIds.Contains(artifact.ItemId) && !_items.ContainsKey(artifact.ItemId))
            {
                return InvalidImport($"Workflow artifact '{artifact.Id}' references missing item '{artifact.ItemId}'.");
            }

            if (artifact.EventId != null && !eventIds.Contains(artifact.EventId) && !_events.Any(evt => evt.Id == artifact.EventId))
            {
                return InvalidImport($"Workflow artifact '{artifact.Id}' references missing event '{artifact.EventId}'.");
            }
        }

        return ScriptWorkflowLedgerResult.Succeeded();
    }

    private static ScriptWorkflowLedgerResult? ValidateRequiredImport(string? value, string name) =>
        TrimToNull(value) == null ? InvalidImport($"{name} is required.") : null;

    private static ScriptWorkflowLedgerResult InvalidImport(string message) =>
        ScriptWorkflowLedgerResult.Failed(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: message));

    private void RebuildEventIdempotencyIndex()
    {
        _eventIdsByIdempotencyKey.Clear();
        foreach (var evt in _events)
        {
            if (evt.ItemId != null && evt.IdempotencyKey != null)
            {
                _eventIdsByIdempotencyKey[EventIdempotencyKey(evt.ItemId, evt.IdempotencyKey)] = evt.Id;
            }
        }
    }

    private void RebuildArtifactIdentityIndex()
    {
        _artifactIdsByIdentity.Clear();
        foreach (var artifact in _artifacts)
        {
            _artifactIdsByIdentity[ArtifactIdentity(artifact.ItemId, artifact.ArtifactKind, artifact.ArtifactRef)] = artifact.Id;
        }
    }

    private static bool IsTerminalRunStatus(string status) =>
        status is "completed" or "failed" or "cancelled" or "canceled";

    private static bool IsTerminalItemState(string state) =>
        state is "completed" or "failed" or "dead" or "skipped" or "cancelled" or "canceled";

    private RunState FromRun(ScriptWorkflowRun run) => new()
    {
        Id = run.Id,
        WorkflowName = run.WorkflowName,
        Status = run.Status,
        Source = run.Source,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        LastError = run.LastError,
        MetadataJson = run.MetadataJson,
        Sequence = _sequence++
    };

    private ItemState FromItem(ScriptWorkflowItem item) => new()
    {
        Id = item.Id,
        WorkflowName = item.WorkflowName,
        ItemKey = item.ItemKey,
        ItemType = item.ItemType,
        RunId = item.RunId,
        Stage = item.Stage,
        State = item.State,
        Priority = item.Priority,
        AttemptCount = item.AttemptCount,
        MaxAttempts = item.MaxAttempts,
        NextRetryAt = item.NextRetryAt,
        LeaseOwner = item.LeaseOwner,
        LeaseExpiresAt = item.LeaseExpiresAt,
        LastError = item.LastError,
        LastErrorType = item.LastErrorType,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        MetadataJson = item.MetadataJson,
        Sequence = _sequence++
    };

    private EventState FromEvent(ScriptWorkflowEvent evt) => new()
    {
        Id = evt.Id,
        RunId = evt.RunId,
        ItemId = evt.ItemId,
        EventType = evt.EventType,
        Stage = evt.Stage,
        State = evt.State,
        Message = evt.Message,
        Error = evt.Error,
        IdempotencyKey = evt.IdempotencyKey,
        CreatedAt = evt.CreatedAt,
        MetadataJson = evt.MetadataJson,
        Sequence = _sequence++
    };

    private ArtifactState FromArtifact(ScriptWorkflowArtifact artifact) => new()
    {
        Id = artifact.Id,
        ItemId = artifact.ItemId,
        EventId = artifact.EventId,
        ArtifactKind = artifact.ArtifactKind,
        ArtifactRef = artifact.ArtifactRef,
        Role = artifact.Role,
        CreatedAt = artifact.CreatedAt,
        MetadataJson = artifact.MetadataJson,
        Sequence = _sequence++
    };

    private ScriptWorkflowLedgerResult<ScriptWorkflowItem> RetryItemCore(ItemState item, DateTimeOffset? nextRetryAt)
    {
        var state = nextRetryAt.HasValue ? "retry_ready" : "pending";
        var transition = ValidateTransition(ToItem(item), item.Stage, state, "retry");
        if (!transition.Success)
        {
            return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Failed(transition.Error!);
        }

        item.State = state;
        item.NextRetryAt = nextRetryAt;
        item.LeaseOwner = null;
        item.LeaseExpiresAt = null;
        item.LastError = null;
        item.LastErrorType = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        InsertEvent(item.Id, item.RunId, "retry_scheduled", item.Stage, item.State, null, null, null, null);
        return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Succeeded(ToItem(item));
    }

    private ScriptWorkflowLedgerResult<ScriptWorkflowItem> CancelItemCore(ItemState item, string? reason, string? metadataJson)
    {
        var transition = ValidateTransition(ToItem(item), item.Stage, "cancelled", "cancel");
        if (!transition.Success)
        {
            return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Failed(transition.Error!);
        }

        item.State = "cancelled";
        item.LeaseOwner = null;
        item.LeaseExpiresAt = null;
        item.NextRetryAt = null;
        item.LastError = reason;
        item.MetadataJson = metadataJson ?? item.MetadataJson;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        InsertEvent(item.Id, item.RunId, "cancelled", item.Stage, item.State, null, reason, null, metadataJson);
        return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Succeeded(ToItem(item));
    }

    private ScriptWorkflowLedgerResult<ScriptWorkflowItem> DeadLetterItemCore(ItemState item, string reason)
    {
        var transition = ValidateTransition(ToItem(item), item.Stage, "dead", "dead-letter");
        if (!transition.Success)
        {
            return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Failed(transition.Error!);
        }

        item.State = "dead";
        item.LeaseOwner = null;
        item.LeaseExpiresAt = null;
        item.NextRetryAt = null;
        item.LastError = reason;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        InsertEvent(item.Id, item.RunId, "dead_lettered", item.Stage, item.State, null, reason, null, null);
        return ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Succeeded(ToItem(item));
    }

    private ScriptWorkflowLedgerResult EnsureLeaseOwnerIfProvided(ItemState item, string? leaseOwner)
    {
        var normalized = TrimToNull(leaseOwner);
        return normalized != null && item.LeaseOwner != normalized
            ? ScriptWorkflowLedgerResult.Failed(Error(
                ScriptWorkflowLedgerErrorCode.LeaseMismatch,
                workflowName: item.WorkflowName,
                runId: item.RunId ?? string.Empty,
                itemId: item.Id,
                message: $"Workflow item '{item.Id}' is not leased by '{normalized}'."))
            : ScriptWorkflowLedgerResult.Succeeded();
    }

    private ItemState ClaimCore(ItemState item, string leaseOwner, DateTimeOffset now, TimeSpan leaseDuration)
    {
        item.State = "running";
        item.LeaseOwner = leaseOwner;
        item.LeaseExpiresAt = now.Add(leaseDuration);
        item.AttemptCount++;
        item.UpdatedAt = now;
        InsertEvent(item.Id, item.RunId, "claimed", item.Stage, item.State, null, null, null, null);
        return item;
    }

    private IEnumerable<ItemState> SelectClaimCandidates(ScriptWorkflowItemQuery query, DateTimeOffset now, int take)
    {
        return _items.Values
            .Where(item => MatchesItemQuery(item, query, retryReadyFilter: false))
            .Where(item =>
            {
                var state = TrimToNull(query.State);
                return state != null ||
                    item.State is "pending" or "retry_ready" ||
                    (item.LeaseOwner != null && item.LeaseExpiresAt <= now);
            })
            .Where(item => item.LeaseOwner == null || item.LeaseExpiresAt == null || item.LeaseExpiresAt <= now)
            .Where(item => item.NextRetryAt == null || item.NextRetryAt <= now)
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Sequence)
            .Take(take);
    }

    private IEnumerable<ItemState> QueryItemStates(ScriptWorkflowItemQuery query)
    {
        return _items.Values
            .Where(item => MatchesItemQuery(item, query, retryReadyFilter: true))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Sequence)
            .Skip(Math.Max(0, query.Skip))
            .Take(ClampTake(query.Take));
    }

    private static bool MatchesItemQuery(ItemState item, ScriptWorkflowItemQuery query, bool retryReadyFilter)
    {
        var workflowName = TrimToNull(query.WorkflowName);
        var runId = TrimToNull(query.RunId);
        var stage = TrimToNull(query.Stage);
        var state = TrimToNull(query.State);

        return (workflowName == null || item.WorkflowName == workflowName) &&
            (runId == null || item.RunId == runId) &&
            (stage == null || item.Stage == stage) &&
            (state == null || item.State == state) &&
            (!retryReadyFilter || !query.RetryReadyAt.HasValue || item.NextRetryAt == null || item.NextRetryAt <= query.RetryReadyAt);
    }

    private ScriptWorkflowLedgerResult ValidateTransition(
        ScriptWorkflowItem current,
        string toStage,
        string toState,
        string operation) =>
        _transitionValidator.ValidateTransition(current, toStage, toState, operation);

    private ScriptWorkflowLedgerResult<ScriptWorkflowEvent> FailedEventIfMissingOrSucceeded(string eventId)
    {
        var existing = _events.FirstOrDefault(evt => evt.Id == eventId);
        return existing == null
            ? FailedEvent(Error(ScriptWorkflowLedgerErrorCode.NotFound, message: $"Workflow event '{eventId}' was not found.")).Result
            : ScriptWorkflowLedgerResult<ScriptWorkflowEvent>.Succeeded(ToEvent(existing));
    }

    private EventState InsertEvent(
        string itemId,
        string? runId,
        string eventType,
        string? stage,
        string? state,
        string? message,
        string? error,
        string? idempotencyKey,
        string? metadataJson)
    {
        var evt = new EventState
        {
            Id = NewId(),
            RunId = runId,
            ItemId = itemId,
            EventType = eventType,
            Stage = stage,
            State = state,
            Message = message,
            Error = error,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            MetadataJson = metadataJson,
            Sequence = _sequence++
        };
        _events.Add(evt);
        return evt;
    }

    private ValueOrError<ItemState> GetRequiredItem(string? itemId)
    {
        var normalizedItemId = RequireItemId(itemId);
        if (normalizedItemId.Error != null)
        {
            return WithError<ItemState>(normalizedItemId.Error);
        }

        return _items.TryGetValue(normalizedItemId.Value!, out var item)
            ? new ValueOrError<ItemState>(item, null)
            : WithError<ItemState>(Error(
                ScriptWorkflowLedgerErrorCode.NotFound,
                itemId: normalizedItemId.Value!,
                message: $"Workflow item '{normalizedItemId.Value}' was not found."));
    }

    private ValueOrError<ClaimValidation> ValidateClaimOptions(ScriptWorkflowClaimOptions? options)
    {
        if (options == null)
        {
            return WithError<ClaimValidation>(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Workflow claim options are required."));
        }

        var leaseOwner = Require(options.LeaseOwner, ScriptWorkflowLedgerErrorCode.InvalidRequest, "leaseOwner", string.Empty, string.Empty, string.Empty);
        if (leaseOwner.Error != null)
        {
            return WithError<ClaimValidation>(leaseOwner.Error);
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            return WithError<ClaimValidation>(Error(ScriptWorkflowLedgerErrorCode.InvalidRequest, message: "Lease duration must be positive."));
        }

        return new ValueOrError<ClaimValidation>(
            new ClaimValidation(
                leaseOwner.Value!,
                options.LeaseDuration,
                options.NowUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow,
                ClampTake(options.Take)),
            null);
    }

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> SucceededRun(ScriptWorkflowRun run) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowRun>.Succeeded(run));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowRun>> FailedRun(ScriptWorkflowLedgerError error) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowRun>.Failed(error));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> SucceededItem(ScriptWorkflowItem item) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Succeeded(item));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> FailedItem(ScriptWorkflowLedgerError error) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowItem>.Failed(error));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowEvent>> SucceededEvent(ScriptWorkflowEvent evt) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowEvent>.Succeeded(evt));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowEvent>> FailedEvent(ScriptWorkflowLedgerError error) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowEvent>.Failed(error));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>> SucceededArtifact(ScriptWorkflowArtifact artifact) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>.Succeeded(artifact));

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>> FailedArtifact(ScriptWorkflowLedgerError error) =>
        Task.FromResult(ScriptWorkflowLedgerResult<ScriptWorkflowArtifact>.Failed(error));

    private static ValueOrError<string> RequireWorkflowName(string? workflowName) =>
        Require(workflowName, ScriptWorkflowLedgerErrorCode.InvalidWorkflowName, "workflowName", string.Empty, string.Empty, string.Empty);

    private static ValueOrError<string> RequireRunId(string? runId) =>
        Require(runId, ScriptWorkflowLedgerErrorCode.InvalidRunId, "runId", string.Empty, runId ?? string.Empty, string.Empty);

    private static ValueOrError<string> RequireItemId(string? itemId) =>
        Require(itemId, ScriptWorkflowLedgerErrorCode.InvalidItemId, "itemId", string.Empty, string.Empty, itemId ?? string.Empty);

    private static ValueOrError<string> Require(
        string? value,
        ScriptWorkflowLedgerErrorCode code,
        string name,
        string? workflowName,
        string? runId,
        string? itemId)
    {
        var normalized = TrimToNull(value);
        return normalized == null
            ? WithError<string>(Error(
                code,
                workflowName ?? string.Empty,
                runId ?? string.Empty,
                itemId ?? string.Empty,
                $"{name} is required."))
            : new ValueOrError<string>(normalized, null);
    }

    private static ValueOrError<T> WithError<T>(ScriptWorkflowLedgerError error) => new(default, error);

    private static ScriptWorkflowLedgerError Error(
        ScriptWorkflowLedgerErrorCode code,
        string workflowName = "",
        string runId = "",
        string itemId = "",
        string message = "") =>
        new(code, workflowName, runId, itemId, message);

    private static int ClampTake(int take)
    {
        if (take <= 0)
        {
            return 0;
        }

        return Math.Min(take, MaxTake);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string ItemKey(string workflowName, string itemKey) => workflowName + "\0" + itemKey;

    private static string EventIdempotencyKey(string itemId, string idempotencyKey) => itemId + "\0" + idempotencyKey;

    private static string ArtifactIdentity(string itemId, string kind, string reference) =>
        itemId + "\0" + kind + "\0" + reference;

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ScriptWorkflowRun ToRun(RunState run) => new()
    {
        Id = run.Id,
        WorkflowName = run.WorkflowName,
        Status = run.Status,
        Source = run.Source,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        LastError = run.LastError,
        MetadataJson = run.MetadataJson
    };

    private static ScriptWorkflowItem ToItem(ItemState item) => new()
    {
        Id = item.Id,
        WorkflowName = item.WorkflowName,
        ItemKey = item.ItemKey,
        ItemType = item.ItemType,
        RunId = item.RunId,
        Stage = item.Stage,
        State = item.State,
        Priority = item.Priority,
        AttemptCount = item.AttemptCount,
        MaxAttempts = item.MaxAttempts,
        NextRetryAt = item.NextRetryAt,
        LeaseOwner = item.LeaseOwner,
        LeaseExpiresAt = item.LeaseExpiresAt,
        LastError = item.LastError,
        LastErrorType = item.LastErrorType,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        MetadataJson = item.MetadataJson
    };

    private static ScriptWorkflowEvent ToEvent(EventState evt) => new()
    {
        Id = evt.Id,
        RunId = evt.RunId,
        ItemId = evt.ItemId,
        EventType = evt.EventType,
        Stage = evt.Stage,
        State = evt.State,
        Message = evt.Message,
        Error = evt.Error,
        IdempotencyKey = evt.IdempotencyKey,
        CreatedAt = evt.CreatedAt,
        MetadataJson = evt.MetadataJson
    };

    private static ScriptWorkflowArtifact ToArtifact(ArtifactState artifact) => new()
    {
        Id = artifact.Id,
        ItemId = artifact.ItemId,
        EventId = artifact.EventId,
        ArtifactKind = artifact.ArtifactKind,
        ArtifactRef = artifact.ArtifactRef,
        Role = artifact.Role,
        CreatedAt = artifact.CreatedAt,
        MetadataJson = artifact.MetadataJson
    };

    private sealed record ValueOrError<T>(T? Value, ScriptWorkflowLedgerError? Error);

    private sealed record ClaimValidation(string LeaseOwner, TimeSpan LeaseDuration, DateTimeOffset Now, int Take);

    private sealed record BulkTargets(
        int Requested,
        IReadOnlyList<ItemState> Items,
        IReadOnlyList<ScriptWorkflowBulkMutationError> Errors);

    private sealed record PrioritizedAttentionItem(int Priority, ScriptWorkflowLedgerAttentionItem Item);

    private sealed record TransitionSchemaOverlay(
        string Status,
        string? Message,
        int AllowedTransitionCount = 0,
        int UnexpectedTransitionCount = 0,
        int SchemaOnlyTransitionCount = 0);

    private sealed class TransitionNodeBuilder
    {
        public TransitionNodeBuilder(string stage, string state)
        {
            Stage = stage;
            State = state;
        }

        public string Stage { get; }
        public string State { get; }
        public int CurrentItemCount { get; set; }
        public int EventCount { get; set; }
        public int ErrorCount { get; set; }
        public string SchemaStatus { get; private set; } = ScriptWorkflowTransitionSchemaStatus.Observed;

        public void MarkSchemaStatus(string status)
        {
            if (SchemaStatus == ScriptWorkflowTransitionSchemaStatus.Unexpected)
            {
                return;
            }

            if (status == ScriptWorkflowTransitionSchemaStatus.Unexpected ||
                SchemaStatus == ScriptWorkflowTransitionSchemaStatus.Observed ||
                SchemaStatus == ScriptWorkflowTransitionSchemaStatus.SchemaOnly)
            {
                SchemaStatus = status;
            }
        }
    }

    private sealed class TransitionEdgeBuilder
    {
        public TransitionEdgeBuilder(string fromStage, string fromState, string toStage, string toState)
        {
            FromStage = fromStage;
            FromState = fromState;
            ToStage = toStage;
            ToState = toState;
        }

        public string FromStage { get; }
        public string FromState { get; }
        public string ToStage { get; }
        public string ToState { get; }
        public int Count { get; set; }
        public int ErrorCount { get; set; }
        public int RetryCount { get; set; }
        public int DeadLetterCount { get; set; }
        public DateTimeOffset? LastSeenAt { get; set; }
        public string? SampleItemId { get; set; }
        public string SchemaStatus { get; set; } = ScriptWorkflowTransitionSchemaStatus.Observed;
    }

    private sealed class FlowQueueEvidenceBuilder
    {
        private readonly HashSet<string> _itemIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _problemItemIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _itemIdsByState = new(StringComparer.Ordinal);

        public FlowQueueEvidenceBuilder(string queueName)
        {
            QueueName = queueName;
        }

        public string QueueName { get; }
        public DateTimeOffset? LastSeenAt { get; private set; }
        public string? SampleItemId { get; private set; }

        public void Add(ItemState item, EventState evt)
        {
            _itemIds.Add(item.Id);
            if (!_itemIdsByState.TryGetValue(item.State, out var stateItems))
            {
                stateItems = new HashSet<string>(StringComparer.Ordinal);
                _itemIdsByState[item.State] = stateItems;
            }

            stateItems.Add(item.Id);
            if (IsProblemFlowEvidenceItem(item))
            {
                _problemItemIds.Add(item.Id);
            }

            if (!LastSeenAt.HasValue || evt.CreatedAt > LastSeenAt.Value)
            {
                LastSeenAt = evt.CreatedAt;
                SampleItemId = item.Id;
            }
        }

        public ScriptWorkflowFlowQueueEvidence ToEvidence() =>
            new(
                QueueName,
                _itemIds.Count,
                _itemIdsByState
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal),
                _problemItemIds.Count,
                LastSeenAt,
                SampleItemId);
    }

    private sealed class RunState
    {
        public string Id { get; init; } = string.Empty;
        public string WorkflowName { get; init; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Source { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string? LastError { get; set; }
        public string? MetadataJson { get; set; }
        public long Sequence { get; init; }
    }

    private sealed class ItemState
    {
        public string Id { get; init; } = string.Empty;
        public string WorkflowName { get; init; } = string.Empty;
        public string ItemKey { get; init; } = string.Empty;
        public string? ItemType { get; set; }
        public string? RunId { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int AttemptCount { get; set; }
        public int? MaxAttempts { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
        public string? LeaseOwner { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public string? LastError { get; set; }
        public string? LastErrorType { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? MetadataJson { get; set; }
        public long Sequence { get; init; }
    }

    private sealed class EventState
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
        public long Sequence { get; init; }
    }

    private sealed class ArtifactState
    {
        public string Id { get; init; } = string.Empty;
        public string ItemId { get; init; } = string.Empty;
        public string? EventId { get; init; }
        public string ArtifactKind { get; init; } = string.Empty;
        public string ArtifactRef { get; init; } = string.Empty;
        public string? Role { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string? MetadataJson { get; init; }
        public long Sequence { get; init; }
    }
}
