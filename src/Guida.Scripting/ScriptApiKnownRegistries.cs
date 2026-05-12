namespace Guida.Scripting;

/// <summary>
/// Well-known public script API registry documents.
/// </summary>
public static class ScriptApiKnownRegistries
{
    /// <summary>
    /// Creates the public registry for extracted host capability adapter surfaces.
    /// </summary>
    public static ScriptApiRegistry CreateExtractedCapabilities()
    {
        var interfaces = new List<ScriptApiInterface>();
        interfaces.AddRange(CreateStoreInterfaces());
        interfaces.AddRange(CreateQueueInterfaces());
        interfaces.AddRange(CreateWorkerInterfaces());
        interfaces.AddRange(CreateWorkflowInterfaces());
        interfaces.AddRange(CreateWorkspaceInterfaces());

        return new ScriptApiRegistry
        {
            Interfaces = interfaces,
            Groups =
            [
                CreateStoreGroup(),
                CreateQueueGroup(),
                CreateWorkersGroup(),
                CreateWorkerGroup(),
                CreateWorkflowGroup(),
                CreateWorkflowsGroup(),
                CreateWorkspaceGroup()
            ]
        };
    }

    private static IReadOnlyList<ScriptApiInterface> CreateStoreInterfaces() =>
    [
        Interface(
            "StoreDoc",
            "A document retrieved from the store",
            Properties(
                Property("key", ScriptApiType.String, description: "Document key"),
                Property("data", ScriptApiType.Any, description: "The stored data object"),
                Property("createdAt", ScriptApiType.String, description: "ISO 8601 creation timestamp"),
                Property("updatedAt", ScriptApiType.String, description: "ISO 8601 last update timestamp"))),
        Interface(
            "StoreListOptions",
            "Options for listing documents",
            Properties(
                Property("limit", ScriptApiType.Number, optional: true, description: "Max results"),
                Property("offset", ScriptApiType.Number, optional: true, description: "Skip N results"),
                Property("sort", ScriptApiType.String, optional: true, description: "Sort field"),
                Property("order", ScriptApiType.String, optional: true, description: "Sort order"))),
        Interface(
            "StoreSearchOptions",
            "Options for searching documents",
            Properties(
                Property("limit", ScriptApiType.Number, optional: true, description: "Max results"),
                Property("offset", ScriptApiType.Number, optional: true, description: "Skip N results")))
    ];

    private static ScriptApiGroup CreateStoreGroup() =>
        Group(
            "StoreApi",
            "store",
            "Workspace-scoped persistent document storage API",
            Functions(
                Function("put", "g.store.put", "store", "Upsert a document into a collection.", ScriptApiType.Void,
                    Parameter("collection", ScriptApiType.String, description: "Collection name"),
                    Parameter("key", ScriptApiType.String, description: "Document key"),
                    Parameter("data", ScriptApiType.Any, description: "Data object to store")),
                Function("get", "g.store.get", "store", "Get a document by key. Returns null if not found.", Type("StoreDoc | null"),
                    Parameter("collection", ScriptApiType.String, description: "Collection name"),
                    Parameter("key", ScriptApiType.String, description: "Document key")),
                Function("list", "g.store.list", "store", "List documents in a collection with pagination.", Type("StoreDoc[]"),
                    Parameter("collection", ScriptApiType.String, description: "Collection name"),
                    Parameter("options", Type("StoreListOptions"), optional: true)),
                Function("search", "g.store.search", "store", "Search documents by text content.", Type("StoreDoc[]"),
                    Parameter("collection", ScriptApiType.String, description: "Collection name"),
                    Parameter("query", ScriptApiType.String, description: "Text to search for across all string values"),
                    Parameter("options", Type("StoreSearchOptions"), optional: true)),
                Function("delete", "g.store.delete", "store", "Delete a document. Returns true if found and deleted.", ScriptApiType.Boolean,
                    Parameter("collection", ScriptApiType.String, description: "Collection name"),
                    Parameter("key", ScriptApiType.String, description: "Document key")),
                Function("count", "g.store.count", "store", "Count documents in a collection.", ScriptApiType.Number,
                    Parameter("collection", ScriptApiType.String, description: "Collection name")),
                Function("clear", "g.store.clear", "store", "Delete all documents in a collection.", ScriptApiType.Void,
                    Parameter("collection", ScriptApiType.String, description: "Collection name")),
                Function("collections", "g.store.collections", "store", "List all collection names in the store.", Type("string[]"))));

    private static IReadOnlyList<ScriptApiInterface> CreateQueueInterfaces() =>
    [
        Interface(
            "QueueEnqueueOptions",
            "Options for enqueuing items",
            Properties(
                Property("priority", ScriptApiType.Number, optional: true, description: "Priority"),
                Property("maxRetries", ScriptApiType.Number, optional: true, description: "Max retry attempts before dead-letter"),
                Property("group", ScriptApiType.String, optional: true, description: "Group key for strategy-based dequeue"))),
        Interface(
            "QueueDequeueOptions",
            "Options for dequeuing items",
            Properties(Property("strategy", ScriptApiType.String, optional: true, description: "Dequeue strategy"))),
        Interface(
            "QueueItem",
            "A dequeued work item",
            Properties(
                Property("id", ScriptApiType.String, description: "Checkout ID"),
                Property("data", ScriptApiType.Any, description: "The stored data object"),
                Property("priority", ScriptApiType.Number, description: "Priority level"),
                Property("groupKey", ScriptApiType.String, optional: true, description: "Group key"),
                Property("attempts", ScriptApiType.Number, description: "Number of processing attempts"),
                Property("maxRetries", ScriptApiType.Number, description: "Max allowed attempts"),
                Property("enqueuedAt", ScriptApiType.String, description: "ISO 8601 enqueue timestamp"),
                Property("lastAttemptAt", ScriptApiType.String, optional: true, description: "ISO 8601 last attempt timestamp"),
                Property("lastError", ScriptApiType.String, optional: true, description: "Last error message"))),
        Interface(
            "DeadLetterItem",
            "A dead-lettered queue item that exceeded max retries",
            Properties(
                Property("deadLetterId", ScriptApiType.String, description: "Item ID"),
                Property("data", ScriptApiType.Any, description: "The stored data object"),
                Property("priority", ScriptApiType.Number, description: "Priority level"),
                Property("groupKey", ScriptApiType.String, optional: true, description: "Group key"),
                Property("attempts", ScriptApiType.Number, description: "Number of processing attempts"),
                Property("maxRetries", ScriptApiType.Number, description: "Max allowed attempts"),
                Property("enqueuedAt", ScriptApiType.String, description: "ISO 8601 enqueue timestamp"),
                Property("lastAttemptAt", ScriptApiType.String, optional: true, description: "ISO 8601 last attempt timestamp"),
                Property("lastError", ScriptApiType.String, optional: true, description: "Last error message"),
                Property("deadLetteredAt", ScriptApiType.String, description: "ISO 8601 dead-letter timestamp"))),
        Interface(
            "QueueListOptions",
            "Options for listing queue items",
            Properties(
                Property("limit", ScriptApiType.Number, optional: true, description: "Max results"),
                Property("offset", ScriptApiType.Number, optional: true, description: "Skip N results"))),
        Interface(
            "WaitForItemOptions",
            "Options for waiting for a queue item",
            Properties(
                Property("timeout", ScriptApiType.Number, optional: true, description: "Timeout in milliseconds"),
                Property("strategy", ScriptApiType.String, optional: true, description: "Dequeue strategy")))
    ];

    private static ScriptApiGroup CreateQueueGroup() =>
        Group(
            "QueueApi",
            "queue",
            "Workspace-scoped persistent work queue with pluggable dequeue strategies",
            Functions(
                Function("enqueue", "g.queue.enqueue", "queue", "Add an item to the queue.", ScriptApiType.Void,
                    Parameter("name", ScriptApiType.String, description: "Queue name"),
                    Parameter("data", ScriptApiType.Any, description: "Data object to enqueue"),
                    Parameter("options", Type("QueueEnqueueOptions"), optional: true)),
                Function("dequeue", "g.queue.dequeue", "queue", "Checkout the next item. Returns null if empty.", Type("QueueItem | null"),
                    Parameter("name", ScriptApiType.String, description: "Queue name"),
                    Parameter("options", Type("QueueDequeueOptions"), optional: true)),
                Function("commit", "g.queue.commit", "queue", "Acknowledge successful processing.", ScriptApiType.Void,
                    Parameter("checkoutId", ScriptApiType.String, description: "Checkout ID from dequeue")),
                Function("abort", "g.queue.abort", "queue", "Return item to queue for retry. Returns true if dead-lettered.", ScriptApiType.Boolean,
                    Parameter("checkoutId", ScriptApiType.String, description: "Checkout ID from dequeue"),
                    Parameter("error", ScriptApiType.String, optional: true, description: "Error message")),
                Function("peek", "g.queue.peek", "queue", "View the next item without checking it out.", Type("QueueItem | null"),
                    Parameter("name", ScriptApiType.String, description: "Queue name")),
                Function("count", "g.queue.count", "queue", "Count pending items in the queue.", ScriptApiType.Number,
                    Parameter("name", ScriptApiType.String, description: "Queue name")),
                Function("clear", "g.queue.clear", "queue", "Remove all items and dead-letter entries for this queue.", ScriptApiType.Void,
                    Parameter("name", ScriptApiType.String, description: "Queue name")),
                Function("list", "g.queue.list", "queue", "List pending items in the queue.", Type("QueueItem[]"),
                    Parameter("name", ScriptApiType.String, description: "Queue name"),
                    Parameter("options", Type("QueueListOptions"), optional: true)),
                Function("queues", "g.queue.queues", "queue", "List all queue names.", Type("string[]")),
                Function("deadLetter", "g.queue.deadLetter", "queue", "List dead-lettered items that exceeded max retries.", Type("DeadLetterItem[]"),
                    Parameter("name", ScriptApiType.String, description: "Queue name"),
                    Parameter("options", Type("QueueListOptions"), optional: true)),
                Function("retry", "g.queue.retry", "queue", "Move a dead-lettered item back to the queue with attempts reset.", ScriptApiType.Void,
                    Parameter("itemId", ScriptApiType.String, description: "Item ID from dead letter list")),
                AsyncFunction("waitForItem", "g.queue.waitForItem", "queue", "Block until an item is available in the queue. Returns null on timeout.", ScriptApiType.Promise(Type("QueueItem | null")),
                    Parameter("name", ScriptApiType.String, description: "Queue name"),
                    Parameter("options", Type("WaitForItemOptions"), optional: true)),
                Function("registerStrategy", "g.queue.registerStrategy", "queue", "Register a custom dequeue strategy.", ScriptApiType.Void,
                    Parameter("name", ScriptApiType.String, description: "Strategy name"),
                    Parameter("fnOrPath", Type("((groups: Record<string, number>, ctx: { lastGroup: string | null, callCount: number, state: Record<string, any> }) => string | null) | string"), description: "Strategy function or workspace script file path"))));

    private static IReadOnlyList<ScriptApiInterface> CreateWorkerInterfaces() =>
    [
        Interface(
            "WorkerConfig",
            "Configuration for a single queue worker",
            Properties(
                Property("queue", ScriptApiType.String, description: "Queue name to process"),
                Property("script", ScriptApiType.String, description: "Script file path"),
                Property("concurrency", ScriptApiType.Number, optional: true, description: "Number of parallel workers"),
                Property("throttle", Type("{ requestsPerMinute?: number }"), optional: true, description: "Rate limiting"),
                Property("dequeue", ScriptApiType.String, optional: true, description: "Dequeue strategy"))),
        Interface(
            "WorkerStartOptions",
            "Options for starting queue workers",
            Properties(
                Property("clearTabs", ScriptApiType.Boolean, optional: true, description: "Close existing tabs before starting"),
                Property("layout", ScriptApiType.String, optional: true, description: "Tab layout"),
                Property("workers", Type("WorkerConfig[]"), description: "Worker definitions"))),
        Interface(
            "WorkerPoolStatus",
            "Status of a worker pool",
            Properties(
                Property("queue", ScriptApiType.String, description: "Queue name"),
                Property("script", ScriptApiType.String, description: "Script path"),
                Property("concurrency", ScriptApiType.Number, description: "Configured concurrency"),
                Property("activeWorkers", ScriptApiType.Number, description: "Currently active workers"),
                Property("processed", ScriptApiType.Number, description: "Successfully processed items"),
                Property("failed", ScriptApiType.Number, description: "Failed items"),
                Property("remaining", ScriptApiType.Number, description: "Remaining items in queue"),
                Property("paused", ScriptApiType.Boolean, description: "Whether workers are paused"),
                Property("activeRunOneCount", ScriptApiType.Number, description: "Active one-shot worker executions"),
                Property("runOneTaskId", ScriptApiType.String, optional: true, description: "Task ID for the latest active one-shot execution"))),
        Interface(
            "WorkerContext",
            "Context available to per-item worker scripts",
            Properties(
                Property("item", Type("QueueItem"), description: "The dequeued item being processed"),
                Property("tabId", ScriptApiType.String, description: "The worker's assigned tab ID"),
                Property("queueName", ScriptApiType.String, description: "The queue name being processed"),
                Property("prefetchTabId", ScriptApiType.String, optional: true, description: "Tab ID of a pre-loaded page for this item"))),
        Interface(
            "WorkerWorkflowContext",
            "Workflow ledger identity extracted from the current worker queue item",
            Properties(
                Property("queueName", ScriptApiType.String, description: "Queue name being processed"),
                Property("queueItemId", ScriptApiType.String, description: "Queue item ID"),
                Property("itemId", ScriptApiType.String, description: "Workflow ledger item ID"),
                Property("workflowName", ScriptApiType.String, description: "Workflow name"),
                Property("itemKey", ScriptApiType.String, description: "Stable workflow item key"),
                Property("runId", ScriptApiType.String, optional: true, description: "Associated run ID"),
                Property("payload", ScriptApiType.Any, description: "Original queued payload from the workflow envelope"))),
        Interface(
            "WorkerWorkflowClaimOptions",
            "Options for claiming the current worker workflow item",
            Properties(
                Property("leaseOwner", ScriptApiType.String, optional: true, description: "Lease owner"),
                Property("leaseDurationMs", ScriptApiType.Number, optional: true, description: "Lease duration in milliseconds"),
                Property("nowUtc", ScriptApiType.String, optional: true, description: "ISO 8601 current time override"))),
        Interface(
            "WorkerWorkflowApi",
            "Ledger helpers for workflow-tracked queue worker items",
            functions: Functions(
                WorkerWorkflowFunction("getContext", "Get workflow ledger identity and original payload for the current queue item.", Type("WorkerWorkflowContext")),
                WorkerWorkflowFunction("getItem", "Get the current workflow ledger item.", Type("WorkflowLedgerItem")),
                WorkerWorkflowFunction("claim", "Claim the current workflow ledger item and mark it running.", Type("WorkflowLedgerItem"), Parameter("options", Type("WorkerWorkflowClaimOptions"), optional: true)),
                WorkerWorkflowFunction("complete", "Mark the current workflow ledger item completed.", Type("WorkflowLedgerItem"), Parameter("options", Type("WorkflowLedgerLeaseOptions"), optional: true)),
                WorkerWorkflowFunction("fail", "Mark the current workflow ledger item failed or dead depending on attempt count.", Type("WorkflowLedgerItem"), Parameter("errorOrOptions", Type("string | WorkflowLedgerFailureOptions"))),
                WorkerWorkflowFunction("release", "Release the current workflow ledger item lease without marking failure.", Type("WorkflowLedgerItem"), Parameter("options", Type("WorkflowLedgerLeaseOptions"), optional: true)),
                WorkerWorkflowFunction("retry", "Move the current workflow ledger item back to a claimable state.", Type("WorkflowLedgerItem"), Parameter("options", Type("WorkflowLedgerRetryOptions"), optional: true)),
                WorkerWorkflowFunction("deadLetter", "Mark the current workflow ledger item dead with a reason.", Type("WorkflowLedgerItem"), Parameter("reasonOrOptions", Type("string | WorkflowLedgerDeadLetterOptions")))))
    ];

    private static ScriptApiGroup CreateWorkersGroup() =>
        Group(
            "WorkersApi",
            "workers",
            "Queue worker pool management for batch processing",
            Functions(
                Function("start", "g.workers.start", "workers", "Start worker pools.", ScriptApiType.Void,
                    Parameter("options", Type("WorkerStartOptions"), description: "Worker pool configuration")),
                Function("stop", "g.workers.stop", "workers", "Stop workers.", ScriptApiType.Void,
                    Parameter("queueName", ScriptApiType.String, optional: true, description: "Stop workers for this queue")),
                Function("pause", "g.workers.pause", "workers", "Pause all workers.", ScriptApiType.Void),
                Function("resume", "g.workers.resume", "workers", "Resume paused workers.", ScriptApiType.Void),
                Function("status", "g.workers.status", "workers", "Get status of all worker pools.", Type("WorkerPoolStatus[]"))));

    private static ScriptApiGroup CreateWorkerGroup() =>
        Group(
            "WorkerApi",
            "worker",
            "Per-item worker script context",
            Functions(
                Function("getContext", "g.worker.getContext", "worker", "Get the current worker context.", Type("WorkerContext"))),
            Properties(Property("workflow", Type("WorkerWorkflowApi"), description: "Workflow ledger helpers for the current worker item")));

    private static IReadOnlyList<ScriptApiInterface> CreateWorkflowInterfaces()
    {
        var interfaces = new List<ScriptApiInterface>
        {
            Interface(
                "WorkflowInfo",
                "Active workflow information",
                Properties(
                    Property("name", ScriptApiType.String, description: "Workflow name"),
                    Property("path", ScriptApiType.String, description: "Workflow folder path"))),
            Interface(
                "WorkflowListItem",
                "Workflow summary from workspace discovery",
                Properties(
                    Property("name", ScriptApiType.String, description: "Workflow name"),
                    Property("enabled", ScriptApiType.Boolean, description: "Whether the workflow is enabled"),
                    Property("hasEvents", ScriptApiType.Boolean, description: "Has event configuration"),
                    Property("hasWorkers", ScriptApiType.Boolean, description: "Has worker configuration"),
                    Property("hasTools", ScriptApiType.Boolean, description: "Has tools"),
                    Property("hasViews", ScriptApiType.Boolean, description: "Has views"),
                    Property("scriptCount", ScriptApiType.Number, description: "Number of script files")))
        };

        interfaces.AddRange(CreateWorkflowLedgerInterfaces());
        return interfaces;
    }

    private static IReadOnlyList<ScriptApiInterface> CreateWorkflowLedgerInterfaces() =>
    [
        Interface("WorkflowLedgerRun", "A durable workflow run", Properties(
            Property("id", ScriptApiType.String, description: "Run ID"),
            Property("workflowName", ScriptApiType.String, description: "Workflow name"),
            Property("status", ScriptApiType.String, description: "Run status"),
            Property("source", ScriptApiType.String, optional: true, description: "Run source"),
            Property("startedAt", ScriptApiType.String, description: "ISO 8601 start timestamp"),
            Property("finishedAt", ScriptApiType.String, optional: true, description: "ISO 8601 finish timestamp"),
            Property("lastError", ScriptApiType.String, optional: true, description: "Last error"),
            Property("metadata", ScriptApiType.Any, optional: true, description: "Workflow metadata"))),
        Interface("WorkflowLedgerItem", "A durable workflow item", Properties(
            Property("id", ScriptApiType.String, description: "Item ID"),
            Property("workflowName", ScriptApiType.String, description: "Workflow name"),
            Property("itemKey", ScriptApiType.String, description: "Stable item key within the workflow"),
            Property("itemType", ScriptApiType.String, optional: true, description: "Item type"),
            Property("runId", ScriptApiType.String, optional: true, description: "Associated run ID"),
            Property("stage", ScriptApiType.String, description: "Current stage"),
            Property("state", ScriptApiType.String, description: "Current state"),
            Property("priority", ScriptApiType.Number, description: "Priority"),
            Property("attemptCount", ScriptApiType.Number, description: "Attempt count"),
            Property("maxAttempts", ScriptApiType.Number, optional: true, description: "Maximum attempts"),
            Property("nextRetryAt", ScriptApiType.String, optional: true, description: "ISO 8601 retry-ready timestamp"),
            Property("leaseOwner", ScriptApiType.String, optional: true, description: "Lease owner"),
            Property("leaseExpiresAt", ScriptApiType.String, optional: true, description: "ISO 8601 lease expiry"),
            Property("lastError", ScriptApiType.String, optional: true, description: "Last error"),
            Property("lastErrorType", ScriptApiType.String, optional: true, description: "Last error type"),
            Property("createdAt", ScriptApiType.String, description: "ISO 8601 creation timestamp"),
            Property("updatedAt", ScriptApiType.String, description: "ISO 8601 update timestamp"),
            Property("metadata", ScriptApiType.Any, optional: true, description: "Workflow item metadata"))),
        Interface("WorkflowLedgerEvent", "A durable workflow event", Properties(
            Property("id", ScriptApiType.String, description: "Event ID"),
            Property("runId", ScriptApiType.String, optional: true, description: "Associated run ID"),
            Property("itemId", ScriptApiType.String, optional: true, description: "Associated item ID"),
            Property("eventType", ScriptApiType.String, description: "Event type"),
            Property("stage", ScriptApiType.String, optional: true, description: "Stage at event time"),
            Property("state", ScriptApiType.String, optional: true, description: "State at event time"),
            Property("message", ScriptApiType.String, optional: true, description: "Message"),
            Property("error", ScriptApiType.String, optional: true, description: "Error"),
            Property("idempotencyKey", ScriptApiType.String, optional: true, description: "Idempotency key"),
            Property("createdAt", ScriptApiType.String, description: "ISO 8601 creation timestamp"),
            Property("metadata", ScriptApiType.Any, optional: true, description: "Workflow event metadata"))),
        Interface("WorkflowLedgerArtifact", "A durable workflow artifact reference", Properties(
            Property("id", ScriptApiType.String, description: "Artifact ID"),
            Property("itemId", ScriptApiType.String, description: "Associated item ID"),
            Property("eventId", ScriptApiType.String, optional: true, description: "Associated event ID"),
            Property("artifactKind", ScriptApiType.String, description: "Artifact kind"),
            Property("artifactRef", ScriptApiType.String, description: "Artifact reference"),
            Property("role", ScriptApiType.String, optional: true, description: "Artifact role"),
            Property("createdAt", ScriptApiType.String, description: "ISO 8601 creation timestamp"),
            Property("metadata", ScriptApiType.Any, optional: true, description: "Workflow artifact metadata"))),
        Interface("WorkflowLedgerRunOptions", "Options for starting a workflow run", Properties(Property("source", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerRunListFilter", "Filters for listing workflow runs", Properties(Property("workflowName", ScriptApiType.String, optional: true), Property("status", ScriptApiType.String, optional: true), Property("skip", ScriptApiType.Number, optional: true), Property("take", ScriptApiType.Number, optional: true))),
        Interface("WorkflowLedgerRunFinishOptions", "Options for finishing a workflow run", Properties(Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerRunFailOptions", "Options for failing a workflow run", Properties(Property("error", ScriptApiType.String), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerItemUpsertInput", "Input for upserting a workflow item", Properties(Property("workflowName", ScriptApiType.String), Property("itemKey", ScriptApiType.String, optional: true), Property("key", ScriptApiType.String, optional: true), Property("itemType", ScriptApiType.String, optional: true), Property("type", ScriptApiType.String, optional: true), Property("runId", ScriptApiType.String, optional: true), Property("stage", ScriptApiType.String), Property("state", ScriptApiType.String), Property("priority", ScriptApiType.Number, optional: true), Property("maxAttempts", ScriptApiType.Number, optional: true), Property("nextRetryAt", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerItemQuery", "Filters for querying workflow items", Properties(Property("workflowName", ScriptApiType.String, optional: true), Property("runId", ScriptApiType.String, optional: true), Property("stage", ScriptApiType.String, optional: true), Property("state", ScriptApiType.String, optional: true), Property("retryReadyAt", ScriptApiType.String, optional: true), Property("skip", ScriptApiType.Number, optional: true), Property("take", ScriptApiType.Number, optional: true))),
        Interface("WorkflowLedgerStateUpdate", "Workflow item state update", Properties(Property("stage", ScriptApiType.String, optional: true), Property("state", ScriptApiType.String), Property("priority", ScriptApiType.Number, optional: true), Property("nextRetryAt", ScriptApiType.String, optional: true), Property("leaseOwner", ScriptApiType.String, optional: true), Property("leaseExpiresAt", ScriptApiType.String, optional: true), Property("lastError", ScriptApiType.String, optional: true), Property("lastErrorType", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerEventAppendInput", "Input for appending a workflow event", Properties(Property("runId", ScriptApiType.String, optional: true), Property("eventType", ScriptApiType.String, optional: true), Property("type", ScriptApiType.String, optional: true), Property("stage", ScriptApiType.String, optional: true), Property("state", ScriptApiType.String, optional: true), Property("message", ScriptApiType.String, optional: true), Property("error", ScriptApiType.String, optional: true), Property("idempotencyKey", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerEventListOptions", "Options for listing workflow item events", Properties(Property("skip", ScriptApiType.Number, optional: true), Property("take", ScriptApiType.Number, optional: true))),
        Interface("WorkflowLedgerArtifactAttachInput", "Input for attaching a workflow artifact reference", Properties(Property("eventId", ScriptApiType.String, optional: true), Property("artifactKind", ScriptApiType.String, optional: true), Property("kind", ScriptApiType.String, optional: true), Property("artifactRef", ScriptApiType.String, optional: true), Property("ref", ScriptApiType.String, optional: true), Property("role", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerItemRef", "Reference to a workflow item by ID or workflow/key", Properties(Property("id", ScriptApiType.String, optional: true), Property("workflowName", ScriptApiType.String, optional: true), Property("itemKey", ScriptApiType.String, optional: true), Property("key", ScriptApiType.String, optional: true))),
        Interface("WorkflowLedgerClaimOptions", "Options for claiming workflow items", Properties(Property("leaseOwner", ScriptApiType.String, optional: true), Property("leaseDurationMs", ScriptApiType.Number, optional: true), Property("nowUtc", ScriptApiType.String, optional: true), Property("take", ScriptApiType.Number, optional: true))),
        Interface("WorkflowLedgerLeaseOptions", "Options for lease-scoped item operations", Properties(Property("leaseOwner", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerFailureOptions", "Options for failing a workflow item", Properties(Property("error", ScriptApiType.String), Property("errorType", ScriptApiType.String, optional: true), Property("leaseOwner", ScriptApiType.String, optional: true), Property("nextRetryAt", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerRetryOptions", "Options for retrying a workflow item", Properties(Property("nextRetryAt", ScriptApiType.String, optional: true))),
        Interface("WorkflowLedgerDeadLetterOptions", "Options for dead-lettering a workflow item", Properties(Property("reason", ScriptApiType.String))),
        Interface("WorkflowLedgerQueueEventOptions", "Options for the ledger event recorded by tracked queue enqueue", Properties(Property("eventType", ScriptApiType.String, optional: true), Property("type", ScriptApiType.String, optional: true), Property("message", ScriptApiType.String, optional: true), Property("idempotencyKey", ScriptApiType.String, optional: true), Property("metadata", ScriptApiType.Any, optional: true))),
        Interface("WorkflowLedgerQueueEnqueueInput", "Input for enqueueing queue work with workflow ledger tracking", Properties(Property("queueName", ScriptApiType.String), Property("payload", ScriptApiType.Any), Property("item", Type("WorkflowLedgerItemUpsertInput")), Property("queue", Type("QueueEnqueueOptions"), optional: true), Property("event", Type("WorkflowLedgerQueueEventOptions"), optional: true))),
        Interface("WorkflowLedgerQueueEnqueueResult", "Result from enqueueing queue work with workflow ledger tracking", Properties(Property("queueName", ScriptApiType.String), Property("item", Type("WorkflowLedgerItem")), Property("event", Type("WorkflowLedgerEvent")))),
        Interface("WorkflowLedgerRunsApi", "Workflow ledger run operations", functions: Functions(
            WorkflowFunction("runs.start", "start", "g.workflow.runs.start", "Start a workflow run.", Type("WorkflowLedgerRun"), Parameter("workflowName", ScriptApiType.String), Parameter("options", Type("WorkflowLedgerRunOptions"), optional: true)),
            WorkflowFunction("runs.get", "get", "g.workflow.runs.get", "Get a workflow run by ID.", Type("WorkflowLedgerRun | null"), Parameter("runId", ScriptApiType.String)),
            WorkflowFunction("runs.list", "list", "g.workflow.runs.list", "List workflow runs.", Type("WorkflowLedgerRun[]"), Parameter("filter", Type("WorkflowLedgerRunListFilter"), optional: true)),
            WorkflowFunction("runs.finish", "finish", "g.workflow.runs.finish", "Mark a workflow run completed.", Type("WorkflowLedgerRun"), Parameter("runId", ScriptApiType.String), Parameter("options", Type("WorkflowLedgerRunFinishOptions"), optional: true)),
            WorkflowFunction("runs.fail", "fail", "g.workflow.runs.fail", "Mark a workflow run failed.", Type("WorkflowLedgerRun"), Parameter("runId", ScriptApiType.String), Parameter("errorOrOptions", Type("string | WorkflowLedgerRunFailOptions"))))),
        Interface("WorkflowLedgerItemsApi", "Workflow ledger item operations", functions: Functions(
            WorkflowFunction("items.upsert", "upsert", "g.workflow.items.upsert", "Create or update a workflow item.", Type("WorkflowLedgerItem"), Parameter("input", Type("WorkflowLedgerItemUpsertInput"))),
            WorkflowFunction("items.get", "get", "g.workflow.items.get", "Get a workflow item by workflow name and key.", Type("WorkflowLedgerItem | null"), Parameter("workflowName", ScriptApiType.String), Parameter("itemKey", ScriptApiType.String)),
            WorkflowFunction("items.getById", "getById", "g.workflow.items.getById", "Get a workflow item by ID.", Type("WorkflowLedgerItem | null"), Parameter("itemId", ScriptApiType.String)),
            WorkflowFunction("items.query", "query", "g.workflow.items.query", "Query workflow items.", Type("WorkflowLedgerItem[]"), Parameter("filter", Type("WorkflowLedgerItemQuery"), optional: true)),
            WorkflowFunction("items.setState", "setState", "g.workflow.items.setState", "Update a workflow item state projection.", Type("WorkflowLedgerItem"), Parameter("itemId", ScriptApiType.String), Parameter("update", Type("WorkflowLedgerStateUpdate"))),
            WorkflowFunction("items.appendEvent", "appendEvent", "g.workflow.items.appendEvent", "Append an event to a workflow item.", Type("WorkflowLedgerEvent"), Parameter("itemId", ScriptApiType.String), Parameter("event", Type("WorkflowLedgerEventAppendInput"))),
            WorkflowFunction("items.getEvents", "getEvents", "g.workflow.items.getEvents", "List events for a workflow item.", Type("WorkflowLedgerEvent[]"), Parameter("itemId", ScriptApiType.String), Parameter("options", Type("WorkflowLedgerEventListOptions"), optional: true)),
            WorkflowFunction("items.attachArtifact", "attachArtifact", "g.workflow.items.attachArtifact", "Attach an artifact reference to a workflow item.", Type("WorkflowLedgerArtifact"), Parameter("itemId", ScriptApiType.String), Parameter("artifact", Type("WorkflowLedgerArtifactAttachInput"))),
            WorkflowFunction("items.enqueue", "enqueue", "g.workflow.items.enqueue", "Upsert a workflow item, enqueue queue work for it, and record a queued ledger event.", Type("WorkflowLedgerQueueEnqueueResult"), Parameter("input", Type("WorkflowLedgerQueueEnqueueInput"))),
            WorkflowFunction("items.getArtifacts", "getArtifacts", "g.workflow.items.getArtifacts", "List artifacts for a workflow item.", Type("WorkflowLedgerArtifact[]"), Parameter("itemId", ScriptApiType.String)),
            WorkflowFunction("items.claimNext", "claimNext", "g.workflow.items.claimNext", "Claim eligible pending or retry-ready workflow items.", Type("WorkflowLedgerItem[]"), Parameter("filter", Type("WorkflowLedgerItemQuery"), optional: true), Parameter("leaseOptions", Type("WorkflowLedgerClaimOptions"), optional: true)),
            WorkflowFunction("items.complete", "complete", "g.workflow.items.complete", "Mark a workflow item completed.", Type("WorkflowLedgerItem"), Parameter("itemRef", Type("string | WorkflowLedgerItemRef")), Parameter("options", Type("WorkflowLedgerLeaseOptions"), optional: true)),
            WorkflowFunction("items.fail", "fail", "g.workflow.items.fail", "Mark a workflow item failed or dead depending on attempt count.", Type("WorkflowLedgerItem"), Parameter("itemRef", Type("string | WorkflowLedgerItemRef")), Parameter("errorOrOptions", Type("string | WorkflowLedgerFailureOptions"))),
            WorkflowFunction("items.release", "release", "g.workflow.items.release", "Release a workflow item lease without marking failure.", Type("WorkflowLedgerItem"), Parameter("itemRef", Type("string | WorkflowLedgerItemRef")), Parameter("options", Type("WorkflowLedgerLeaseOptions"), optional: true)),
            WorkflowFunction("items.retry", "retry", "g.workflow.items.retry", "Move a failed or dead workflow item back to a claimable state.", Type("WorkflowLedgerItem"), Parameter("itemRef", Type("string | WorkflowLedgerItemRef")), Parameter("options", Type("WorkflowLedgerRetryOptions"), optional: true)),
            WorkflowFunction("items.deadLetter", "deadLetter", "g.workflow.items.deadLetter", "Mark a workflow item dead with a reason.", Type("WorkflowLedgerItem"), Parameter("itemRef", Type("string | WorkflowLedgerItemRef")), Parameter("reasonOrOptions", Type("string | WorkflowLedgerDeadLetterOptions")))))
    ];

    private static ScriptApiGroup CreateWorkflowGroup() =>
        Group(
            "WorkflowLedgerApi",
            "workflow",
            "Workspace-scoped durable workflow ledger API",
            properties: Properties(
                Property("runs", Type("WorkflowLedgerRunsApi"), description: "Workflow run operations"),
                Property("items", Type("WorkflowLedgerItemsApi"), description: "Workflow item operations")));

    private static ScriptApiGroup CreateWorkflowsGroup() =>
        Group(
            "WorkflowsApi",
            "workflows",
            "Workflow inspection and switching",
            Functions(
                Function("getActive", "g.workflows.getActive", "workflows", "Get the active workflow's name and path, or null if no workflow is active.", Type("WorkflowInfo | null")),
                Function("list", "g.workflows.list", "workflows", "List all workflows in the current workspace.", Type("WorkflowListItem[]")),
                Function("switch", "g.workflows.switch", "workflows", "Request a workflow switch.", ScriptApiType.Void,
                    Parameter("name", ScriptApiType.String, description: "Workflow name to switch to"))));

    private static IReadOnlyList<ScriptApiInterface> CreateWorkspaceInterfaces() =>
    [
        Interface("WorkspaceEntry", "Metadata for a logical workspace entry", Properties(
            Property("path", ScriptApiType.String, description: "Normalized logical workspace path"),
            Property("name", ScriptApiType.String, description: "Display name"),
            Property("kind", Type("\"file\" | \"directory\""), description: "Entry kind"),
            Property("length", ScriptApiType.Number, optional: true, description: "Byte length for file entries"),
            Property("lastModifiedAt", ScriptApiType.String, optional: true, description: "ISO 8601 last modified timestamp"))),
        Interface("WorkspaceFileContent", "Content read from a logical workspace file", Properties(
            Property("path", ScriptApiType.String, description: "Normalized logical workspace path"),
            Property("content", ScriptApiType.String, description: "Text content"))),
        Interface("WorkspaceWriteOptions", "Options for writing a workspace file", Properties(
            Property("overwrite", ScriptApiType.Boolean, optional: true, description: "Whether an existing file may be overwritten"),
            Property("createDirectories", ScriptApiType.Boolean, optional: true, description: "Whether missing parent directories may be created")))
    ];

    private static ScriptApiGroup CreateWorkspaceGroup() =>
        Group(
            "WorkspaceApi",
            "workspace",
            "Logical workspace file access",
            Functions(
                Function("getEntry", "g.workspace.getEntry", "workspace", "Get metadata for a logical workspace path.", Type("WorkspaceEntry | null"),
                    Parameter("path", ScriptApiType.String, description: "Logical workspace path")),
                Function("list", "g.workspace.list", "workspace", "List entries below a logical workspace directory path.", Type("WorkspaceEntry[]"),
                    Parameter("path", ScriptApiType.String, description: "Logical workspace directory path")),
                Function("readFile", "g.workspace.readFile", "workspace", "Read text content from a logical workspace file.", Type("WorkspaceFileContent"),
                    Parameter("path", ScriptApiType.String, description: "Logical workspace file path")),
                Function("writeFile", "g.workspace.writeFile", "workspace", "Write text content to a logical workspace file.", ScriptApiType.Void,
                    Parameter("path", ScriptApiType.String, description: "Logical workspace file path"),
                    Parameter("content", ScriptApiType.String, description: "Text content"),
                    Parameter("options", Type("WorkspaceWriteOptions"), optional: true))));

    private static ScriptApiFunction WorkerWorkflowFunction(
        string name,
        string description,
        ScriptApiType returnType,
        params ScriptApiParameter[] parameters) =>
        Function(name, $"g.worker.workflow.{name}", "worker.workflow", description, returnType, parameters: parameters);

    private static ScriptApiFunction WorkflowFunction(
        string namespaceName,
        string name,
        string fullName,
        string description,
        ScriptApiType returnType,
        params ScriptApiParameter[] parameters) =>
        Function(name, fullName, "workflow", description, returnType, parameters: parameters);

    private static ScriptApiType Type(string name) => ScriptApiType.Custom(name);

    private static ScriptApiInterface Interface(
        string name,
        string description,
        IReadOnlyList<ScriptApiProperty>? properties = null,
        IReadOnlyList<ScriptApiFunction>? functions = null) =>
        new()
        {
            Name = name,
            Description = description,
            Properties = properties ?? Array.Empty<ScriptApiProperty>(),
            Functions = functions ?? Array.Empty<ScriptApiFunction>()
        };

    private static ScriptApiGroup Group(
        string name,
        string propertyName,
        string description,
        IReadOnlyList<ScriptApiFunction>? functions = null,
        IReadOnlyList<ScriptApiProperty>? properties = null) =>
        new()
        {
            Name = name,
            PropertyName = propertyName,
            Description = description,
            Functions = functions ?? Array.Empty<ScriptApiFunction>(),
            Properties = properties ?? Array.Empty<ScriptApiProperty>()
        };

    private static ScriptApiProperty Property(
        string name,
        ScriptApiType type,
        bool optional = false,
        string? description = null) =>
        new(name, type, optional, description);

    private static ScriptApiParameter Parameter(
        string name,
        ScriptApiType type,
        bool optional = false,
        string? description = null) =>
        new(name, type, optional, description);

    private static ScriptApiFunction Function(
        string name,
        string fullName,
        string namespaceName,
        string description,
        ScriptApiType returnType,
        params ScriptApiParameter[] parameters) =>
        new()
        {
            Name = name,
            FullName = fullName,
            Namespace = namespaceName,
            Description = description,
            ReturnType = returnType,
            IsAsync = false,
            Parameters = parameters
        };

    private static ScriptApiFunction AsyncFunction(
        string name,
        string fullName,
        string namespaceName,
        string description,
        ScriptApiType returnType,
        params ScriptApiParameter[] parameters) =>
        new()
        {
            Name = name,
            FullName = fullName,
            Namespace = namespaceName,
            Description = description,
            ReturnType = returnType,
            IsAsync = true,
            Parameters = parameters
        };

    private static IReadOnlyList<ScriptApiProperty> Properties(params ScriptApiProperty[] properties) => properties;

    private static IReadOnlyList<ScriptApiFunction> Functions(params ScriptApiFunction[] functions) => functions;
}
