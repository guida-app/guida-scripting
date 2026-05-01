using System.Collections.ObjectModel;

namespace Guida.Scripting;

/// <summary>
/// Selects available items for an in-memory queue claim operation.
/// </summary>
public delegate IReadOnlyList<ScriptQueueItem> ScriptQueueDequeueStrategy(
    IReadOnlyList<ScriptQueueItem> availableItems,
    ScriptQueueClaimOptions options);

/// <summary>
/// In-memory script queue for tests, samples, and simple hosts.
/// </summary>
public sealed class ScriptInMemoryQueue : IScriptQueue
{
    private readonly ScriptQueueDequeueStrategy _dequeueStrategy;
    private readonly Dictionary<string, List<QueueItemState>> _queues = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _nextId;
    private long _nextSequence;

    /// <summary>
    /// Creates an in-memory queue with enqueue-time dequeue ordering.
    /// </summary>
    public ScriptInMemoryQueue()
        : this(null)
    {
    }

    /// <summary>
    /// Creates an in-memory queue with an optional custom dequeue strategy.
    /// </summary>
    public ScriptInMemoryQueue(ScriptQueueDequeueStrategy? dequeueStrategy)
    {
        _dequeueStrategy = dequeueStrategy ?? DefaultDequeueStrategy;
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult<ScriptQueueItem>> EnqueueAsync(
        string queueName,
        ReadOnlyMemory<byte> payload,
        ScriptQueueEnqueueOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queueValidation = ValidateQueueName(queueName);
        if (!queueValidation.Success)
        {
            return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Failed(queueValidation.Error!));
        }

        options ??= new ScriptQueueEnqueueOptions();
        var itemId = options.ItemId ?? CreateItemId();
        var itemValidation = ValidateItemId(itemId);
        if (!itemValidation.Success)
        {
            return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Failed(itemValidation.Error!));
        }

        lock (_gate)
        {
            var queue = GetOrCreateQueue(queueName);
            if (queue.Any(item => item.Id == itemId))
            {
                return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Failed(
                    Failed(
                        ScriptQueueErrorCode.AlreadyExists,
                        queueName,
                        itemId,
                        $"Queue item '{itemId}' already exists in queue '{queueName}'.")));
            }

            var now = DateTimeOffset.UtcNow;
            var availableAt = options.AvailableAt ??
                (options.Delay is null ? null : now.Add(options.Delay.Value));
            var state = new QueueItemState(
                itemId,
                queueName,
                Copy(payload),
                options.ContentType,
                now,
                availableAt,
                claimedAt: null,
                invisibleUntil: null,
                attemptCount: 0,
                sequence: _nextSequence++);

            queue.Add(state);

            return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Succeeded(ToItem(state)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ClaimAsync(
        string queueName,
        ScriptQueueClaimOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queueValidation = ValidateQueueName(queueName);
        if (!queueValidation.Success)
        {
            return Task.FromResult(ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>.Failed(queueValidation.Error!));
        }

        options ??= new ScriptQueueClaimOptions();
        if (options.MaxItemCount <= 0)
        {
            return Task.FromResult(ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>.Succeeded(
                Array.Empty<ScriptQueueItem>()));
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var queue = GetOrCreateQueue(queueName);
            ExpireClaims(queue, now);

            var availableStates = queue
                .Where(item => IsAvailable(item, now))
                .ToArray();
            var availableItems = availableStates
                .Select(ToItem)
                .ToArray();
            var selectedItems = _dequeueStrategy(
                    new ReadOnlyCollection<ScriptQueueItem>(availableItems),
                    options)
                .Where(item => item.QueueName == queueName)
                .Take(options.MaxItemCount)
                .ToArray();
            var selectedIds = new HashSet<string>(selectedItems.Select(item => item.Id), StringComparer.Ordinal);
            var claimed = new List<ScriptQueueItem>(selectedIds.Count);
            var invisibleUntil = options.VisibilityTimeout is null
                ? (DateTimeOffset?)null
                : now.Add(options.VisibilityTimeout.Value);

            foreach (var state in availableStates)
            {
                if (!selectedIds.Remove(state.Id))
                {
                    continue;
                }

                state.ClaimedAt = now;
                state.InvisibleUntil = invisibleUntil;
                state.AttemptCount++;
                claimed.Add(ToItem(state));
            }

            return Task.FromResult(ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>.Succeeded(
                new ReadOnlyCollection<ScriptQueueItem>(claimed)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult> CompleteAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateQueueAndItem(queueName, itemId);
        if (!validation.Success)
        {
            return Task.FromResult(validation);
        }

        lock (_gate)
        {
            var queue = GetOrCreateQueue(queueName);
            var removed = queue.RemoveAll(item => item.Id == itemId);
            if (removed == 0)
            {
                return Task.FromResult(ScriptQueueResult.Failed(
                    Failed(
                        ScriptQueueErrorCode.NotFound,
                        queueName,
                        itemId,
                        $"Queue item '{itemId}' was not found in queue '{queueName}'.")));
            }
        }

        return Task.FromResult(ScriptQueueResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult> AbandonAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateQueueAndItem(queueName, itemId);
        if (!validation.Success)
        {
            return Task.FromResult(validation);
        }

        lock (_gate)
        {
            var state = FindState(queueName, itemId);
            if (state is null)
            {
                return Task.FromResult(ScriptQueueResult.Failed(
                    Failed(
                        ScriptQueueErrorCode.NotFound,
                        queueName,
                        itemId,
                        $"Queue item '{itemId}' was not found in queue '{queueName}'.")));
            }

            state.ClaimedAt = null;
            state.InvisibleUntil = null;
        }

        return Task.FromResult(ScriptQueueResult.Succeeded());
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult<ScriptQueueItem>> GetAsync(
        string queueName,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateQueueAndItem(queueName, itemId);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Failed(validation.Error!));
        }

        lock (_gate)
        {
            var state = FindState(queueName, itemId);
            if (state is null)
            {
                return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Failed(
                    Failed(
                        ScriptQueueErrorCode.NotFound,
                        queueName,
                        itemId,
                        $"Queue item '{itemId}' was not found in queue '{queueName}'.")));
            }

            return Task.FromResult(ScriptQueueResult<ScriptQueueItem>.Succeeded(ToItem(state)));
        }
    }

    /// <inheritdoc />
    public Task<ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>> ListAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = ValidateQueueName(queueName);
        if (!validation.Success)
        {
            return Task.FromResult(ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>.Failed(validation.Error!));
        }

        lock (_gate)
        {
            var queue = GetOrCreateQueue(queueName);
            var items = queue
                .OrderBy(item => item.Sequence)
                .Select(ToItem)
                .ToArray();

            return Task.FromResult(ScriptQueueResult<IReadOnlyList<ScriptQueueItem>>.Succeeded(
                new ReadOnlyCollection<ScriptQueueItem>(items)));
        }
    }

    private static IReadOnlyList<ScriptQueueItem> DefaultDequeueStrategy(
        IReadOnlyList<ScriptQueueItem> availableItems,
        ScriptQueueClaimOptions options) =>
        availableItems
            .Take(options.MaxItemCount)
            .ToArray();

    private static bool IsAvailable(QueueItemState item, DateTimeOffset now)
    {
        if (item.AvailableAt is { } availableAt && availableAt > now)
        {
            return false;
        }

        return item.ClaimedAt is null || item.InvisibleUntil is not null && item.InvisibleUntil <= now;
    }

    private static void ExpireClaims(IEnumerable<QueueItemState> queue, DateTimeOffset now)
    {
        foreach (var item in queue.Where(item => item.ClaimedAt is not null && item.InvisibleUntil <= now))
        {
            item.ClaimedAt = null;
            item.InvisibleUntil = null;
        }
    }

    private QueueItemState? FindState(string queueName, string itemId) =>
        _queues.TryGetValue(queueName, out var queue)
            ? queue.FirstOrDefault(item => item.Id == itemId)
            : null;

    private List<QueueItemState> GetOrCreateQueue(string queueName)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return queue;
        }

        queue = [];
        _queues[queueName] = queue;
        return queue;
    }

    private string CreateItemId() => Interlocked.Increment(ref _nextId).ToString("D", null);

    private static ScriptQueueResult ValidateQueueAndItem(string? queueName, string? itemId)
    {
        var queueValidation = ValidateQueueName(queueName);
        if (!queueValidation.Success)
        {
            return queueValidation;
        }

        return ValidateItemId(itemId);
    }

    private static ScriptQueueResult ValidateQueueName(string? queueName)
    {
        if (string.IsNullOrWhiteSpace(queueName))
        {
            return ScriptQueueResult.Failed(
                Failed(
                    ScriptQueueErrorCode.InvalidQueueName,
                    queueName ?? string.Empty,
                    string.Empty,
                    "Queue name cannot be empty."));
        }

        return ScriptQueueResult.Succeeded();
    }

    private static ScriptQueueResult ValidateItemId(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return ScriptQueueResult.Failed(
                Failed(
                    ScriptQueueErrorCode.InvalidItemId,
                    string.Empty,
                    itemId ?? string.Empty,
                    "Queue item id cannot be empty."));
        }

        return ScriptQueueResult.Succeeded();
    }

    private static ScriptQueueError Failed(
        ScriptQueueErrorCode code,
        string queueName,
        string itemId,
        string message) =>
        new(code, queueName, itemId, message);

    private static ScriptQueueItem ToItem(QueueItemState item) =>
        new()
        {
            Id = item.Id,
            QueueName = item.QueueName,
            Payload = Copy(item.Payload),
            ContentType = item.ContentType,
            EnqueuedAt = item.EnqueuedAt,
            AvailableAt = item.AvailableAt,
            ClaimedAt = item.ClaimedAt,
            AttemptCount = item.AttemptCount
        };

    private static byte[] Copy(ReadOnlyMemory<byte> content) => content.ToArray();

    private sealed class QueueItemState
    {
        public QueueItemState(
            string id,
            string queueName,
            byte[] payload,
            string? contentType,
            DateTimeOffset enqueuedAt,
            DateTimeOffset? availableAt,
            DateTimeOffset? claimedAt,
            DateTimeOffset? invisibleUntil,
            int attemptCount,
            long sequence)
        {
            Id = id;
            QueueName = queueName;
            Payload = payload;
            ContentType = contentType;
            EnqueuedAt = enqueuedAt;
            AvailableAt = availableAt;
            ClaimedAt = claimedAt;
            InvisibleUntil = invisibleUntil;
            AttemptCount = attemptCount;
            Sequence = sequence;
        }

        public string Id { get; }

        public string QueueName { get; }

        public byte[] Payload { get; }

        public string? ContentType { get; }

        public DateTimeOffset EnqueuedAt { get; }

        public DateTimeOffset? AvailableAt { get; }

        public DateTimeOffset? ClaimedAt { get; set; }

        public DateTimeOffset? InvisibleUntil { get; set; }

        public int AttemptCount { get; set; }

        public long Sequence { get; }
    }
}
