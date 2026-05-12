using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptWorkflowLedgerTests
{
    [Fact]
    public void Workflow_ledger_can_be_registered_and_retrieved_from_host_context()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var context = ScriptHostContext.Empty.WithCapability<IScriptWorkflowLedger>(ledger);

        Assert.True(context.TryGetCapability<IScriptWorkflowLedger>(out var found));
        Assert.Same(ledger, found);
        Assert.Same(ledger, context.GetCapability<IScriptWorkflowLedger>());
    }

    [Fact]
    public void Missing_workflow_ledger_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptWorkflowLedger>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptWorkflowLedger>(out _));
        Assert.Equal(typeof(IScriptWorkflowLedger).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptWorkflowLedger), result.Error);
    }

    [Fact]
    public async Task Runs_roundtrip_lifecycle_and_list_filters()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();

        var run = await ledger.StartRunAsync(
            "crawl",
            new ScriptWorkflowRunOptions { Source = "manual", MetadataJson = """{"batch":1}""" });
        var other = await ledger.StartRunAsync("other");
        var finished = await ledger.FinishRunAsync(
            run.Value!.Id,
            new ScriptWorkflowRunFinishOptions { MetadataJson = """{"done":true}""" });
        var failedRun = await ledger.StartRunAsync("crawl");
        var failed = await ledger.FailRunAsync(
            failedRun.Value!.Id,
            new ScriptWorkflowRunFailOptions { Error = "boom", MetadataJson = """{"failed":true}""" });
        var cancelledRun = await ledger.StartRunAsync("crawl");
        var cancelled = await ledger.CancelRunAsync(
            cancelledRun.Value!.Id,
            new ScriptWorkflowRunCancelOptions { Reason = "stop", MetadataJson = """{"cancelled":true}""" });
        var completedRuns = await ledger.ListRunsAsync(new ScriptWorkflowRunQuery { WorkflowName = "crawl", Status = "completed" });

        Assert.True(run.Success, run.Error?.Message);
        Assert.True(finished.Success, finished.Error?.Message);
        Assert.True(failed.Success, failed.Error?.Message);
        Assert.True(cancelled.Success, cancelled.Error?.Message);
        Assert.True(other.Success, other.Error?.Message);
        Assert.Equal("manual", run.Value.Source);
        Assert.Equal("completed", finished.Value!.Status);
        Assert.NotNull(finished.Value.FinishedAt);
        Assert.Equal("""{"done":true}""", finished.Value.MetadataJson);
        Assert.Equal("failed", failed.Value!.Status);
        Assert.Equal("boom", failed.Value.LastError);
        Assert.Equal("cancelled", cancelled.Value!.Status);
        Assert.Equal("stop", cancelled.Value.LastError);
        Assert.Single(completedRuns.Value!);
        Assert.Equal(run.Value.Id, completedRuns.Value![0].Id);
    }

    [Fact]
    public async Task Items_upsert_get_query_and_set_state()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var run = await ledger.StartRunAsync("crawl");

        var item = await Upsert(
            ledger,
            "url-1",
            runId: run.Value!.Id,
            stage: "fetch",
            state: "pending",
            priority: 3,
            metadataJson: """{"url":"https://example.com"}""");
        var updated = await ledger.UpsertItemAsync(new ScriptWorkflowItemUpsert
        {
            WorkflowName = "crawl",
            ItemKey = "url-1",
            ItemType = "page",
            RunId = run.Value.Id,
            Stage = "parse",
            State = "ready",
            Priority = 9,
            MaxAttempts = 5,
            MetadataJson = """{"parsed":false}"""
        });
        var byKey = await ledger.GetItemAsync("crawl", "url-1");
        var byId = await ledger.GetItemByIdAsync(item.Value!.Id);
        var queried = await ledger.QueryItemsAsync(new ScriptWorkflowItemQuery
        {
            WorkflowName = "crawl",
            RunId = run.Value.Id,
            Stage = "parse",
            State = "ready"
        });
        var state = await ledger.SetItemStateAsync(
            item.Value.Id,
            new ScriptWorkflowStateUpdate
            {
                Stage = "review",
                State = "pending",
                Priority = 1,
                MetadataJson = """{"review":true}"""
            });

        Assert.True(updated.Success, updated.Error?.Message);
        Assert.Equal(item.Value.Id, updated.Value!.Id);
        Assert.Equal(item.Value.CreatedAt, updated.Value.CreatedAt);
        Assert.Equal("page", updated.Value.ItemType);
        Assert.Equal(5, updated.Value.MaxAttempts);
        Assert.Equal(byKey.Value!.Id, byId.Value!.Id);
        Assert.Single(queried.Value!);
        Assert.Equal("review", state.Value!.Stage);
        Assert.Equal("pending", state.Value.State);
        Assert.Equal(1, state.Value.Priority);
        Assert.Equal("""{"review":true}""", state.Value.MetadataJson);
    }

    [Fact]
    public async Task Query_items_filters_retry_ready_and_orders_by_priority_then_created()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var now = DateTimeOffset.UtcNow;
        await Upsert(ledger, "low", state: "retry_ready", priority: 1, nextRetryAt: now.AddMinutes(-5));
        await Upsert(ledger, "high", state: "retry_ready", priority: 10, nextRetryAt: now.AddMinutes(-5));
        await Upsert(ledger, "future", state: "retry_ready", priority: 20, nextRetryAt: now.AddMinutes(5));
        await Upsert(ledger, "other", workflowName: "other", state: "retry_ready", priority: 30, nextRetryAt: now.AddMinutes(-5));

        var result = await ledger.QueryItemsAsync(new ScriptWorkflowItemQuery
        {
            WorkflowName = "crawl",
            State = "retry_ready",
            RetryReadyAt = now
        });

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(["high", "low"], result.Value!.Select(item => item.ItemKey).ToArray());
    }

    [Fact]
    public async Task Events_and_artifacts_preserve_order_and_dedupe()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var item = await Upsert(ledger, "url-1");

        var first = await ledger.AppendEventAsync(
            item.Value!.Id,
            new ScriptWorkflowEventAppend
            {
                EventType = "snapshot_saved",
                IdempotencyKey = "snap-1",
                MetadataJson = """{"snapshotId":"s1"}"""
            });
        var duplicate = await ledger.AppendEventAsync(
            item.Value.Id,
            new ScriptWorkflowEventAppend { EventType = "other", IdempotencyKey = "snap-1" });
        await ledger.AppendEventAsync(item.Value.Id, new ScriptWorkflowEventAppend { EventType = "parsed" });
        var events = await ledger.GetEventsForItemAsync(item.Value.Id);
        var artifact = await ledger.AttachArtifactAsync(
            item.Value.Id,
            new ScriptWorkflowArtifactAttach
            {
                EventId = first.Value!.Id,
                ArtifactKind = "file",
                ArtifactRef = "snapshots/url-1.html",
                Role = "snapshot",
                MetadataJson = """{"bytes":42}"""
            });
        var artifactAgain = await ledger.AttachArtifactAsync(
            item.Value.Id,
            new ScriptWorkflowArtifactAttach
            {
                ArtifactKind = "file",
                ArtifactRef = "snapshots/url-1.html",
                Role = "other"
            });
        var artifacts = await ledger.GetArtifactsForItemAsync(item.Value.Id);

        Assert.Equal(first.Value.Id, duplicate.Value!.Id);
        Assert.Equal("snapshot_saved", duplicate.Value.EventType);
        Assert.Equal(["snapshot_saved", "parsed"], events.Value!.Select(evt => evt.EventType).ToArray());
        Assert.Equal(artifact.Value!.Id, artifactAgain.Value!.Id);
        Assert.Single(artifacts.Value!);
        Assert.Equal("""{"bytes":42}""", artifacts.Value![0].MetadataJson);
    }

    [Fact]
    public async Task Claim_next_claim_item_complete_release_and_lease_mismatch()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        await Upsert(ledger, "low", priority: 1);
        await Upsert(ledger, "high", priority: 10);

        var claimed = await ledger.ClaimNextAsync(
            new ScriptWorkflowItemQuery { WorkflowName = "crawl" },
            new ScriptWorkflowClaimOptions
            {
                LeaseOwner = "worker",
                LeaseDuration = TimeSpan.FromMinutes(5),
                Take = 1
            });
        var hidden = await ledger.ClaimNextAsync(
            new ScriptWorkflowItemQuery { WorkflowName = "crawl", State = "running" },
            new ScriptWorkflowClaimOptions
            {
                LeaseOwner = "worker-2",
                LeaseDuration = TimeSpan.FromMinutes(5),
                Take = 1
            });
        var wrongLease = await ledger.CompleteItemAsync(
            claimed.Value![0].Id,
            new ScriptWorkflowItemCompleteOptions { LeaseOwner = "other" });
        var completed = await ledger.CompleteItemAsync(
            claimed.Value[0].Id,
            new ScriptWorkflowItemCompleteOptions { LeaseOwner = "worker", MetadataJson = """{"done":true}""" });
        var specific = await ledger.ClaimItemAsync(
            (await ledger.GetItemAsync("crawl", "low")).Value!.Id,
            new ScriptWorkflowClaimOptions { LeaseOwner = "worker", LeaseDuration = TimeSpan.FromMinutes(5) });
        var released = await ledger.ReleaseItemAsync(specific.Value!.Id, "worker");

        Assert.Single(claimed.Value);
        Assert.Equal("high", claimed.Value[0].ItemKey);
        Assert.Equal("running", claimed.Value[0].State);
        Assert.Equal("worker", claimed.Value[0].LeaseOwner);
        Assert.Equal(1, claimed.Value[0].AttemptCount);
        Assert.Empty(hidden.Value!);
        Assert.False(wrongLease.Success);
        Assert.Equal(ScriptWorkflowLedgerErrorCode.LeaseMismatch, wrongLease.Error?.Code);
        Assert.Equal("completed", completed.Value!.State);
        Assert.Null(completed.Value.LeaseOwner);
        Assert.Equal("""{"done":true}""", completed.Value.MetadataJson);
        Assert.Equal("pending", released.Value!.State);
        Assert.Null(released.Value.LeaseOwner);
    }

    [Fact]
    public async Task Expired_leases_are_claimable_again()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var now = DateTimeOffset.UtcNow;
        await Upsert(ledger, "url-1");

        var first = await ledger.ClaimNextAsync(
            new ScriptWorkflowItemQuery { WorkflowName = "crawl" },
            new ScriptWorkflowClaimOptions
            {
                LeaseOwner = "worker-1",
                LeaseDuration = TimeSpan.FromMinutes(1),
                NowUtc = now.AddMinutes(-10)
            });
        var second = await ledger.ClaimNextAsync(
            new ScriptWorkflowItemQuery { WorkflowName = "crawl" },
            new ScriptWorkflowClaimOptions
            {
                LeaseOwner = "worker-2",
                LeaseDuration = TimeSpan.FromMinutes(1),
                NowUtc = now
            });

        Assert.Single(first.Value!);
        Assert.Single(second.Value!);
        Assert.Equal(first.Value![0].Id, second.Value![0].Id);
        Assert.Equal("worker-2", second.Value[0].LeaseOwner);
        Assert.Equal(2, second.Value[0].AttemptCount);
    }

    [Fact]
    public async Task Failure_retry_and_dead_letter_states_match_attempt_rules()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var retryItem = await Upsert(ledger, "retry", maxAttempts: 2);
        var deadItem = await Upsert(ledger, "dead", maxAttempts: 1);

        var retryClaim = await ledger.ClaimItemAsync(
            retryItem.Value!.Id,
            new ScriptWorkflowClaimOptions { LeaseOwner = "worker", LeaseDuration = TimeSpan.FromMinutes(1) });
        var retry = await ledger.FailItemAsync(
            retryClaim.Value!.Id,
            new ScriptWorkflowItemFailureOptions
            {
                Error = "temporary",
                ErrorType = "Transient",
                LeaseOwner = "worker",
                MetadataJson = """{"retry":true}"""
            });
        var deadClaim = await ledger.ClaimItemAsync(
            deadItem.Value!.Id,
            new ScriptWorkflowClaimOptions { LeaseOwner = "worker", LeaseDuration = TimeSpan.FromMinutes(1) });
        var dead = await ledger.FailItemAsync(
            deadClaim.Value!.Id,
            new ScriptWorkflowItemFailureOptions { Error = "permanent", LeaseOwner = "worker" });
        var pending = await ledger.RetryItemAsync(dead.Value!.Id);
        var scheduled = await ledger.RetryItemAsync(pending.Value!.Id, DateTimeOffset.UtcNow.AddMinutes(10));
        var manualDead = await ledger.DeadLetterItemAsync(scheduled.Value!.Id, "bad input");

        Assert.Equal("retry_ready", retry.Value!.State);
        Assert.Equal("temporary", retry.Value.LastError);
        Assert.Equal("Transient", retry.Value.LastErrorType);
        Assert.NotNull(retry.Value.NextRetryAt);
        Assert.Equal("""{"retry":true}""", retry.Value.MetadataJson);
        Assert.Equal("dead", dead.Value.State);
        Assert.Equal("permanent", dead.Value.LastError);
        Assert.Null(dead.Value.NextRetryAt);
        Assert.Equal("pending", pending.Value.State);
        Assert.Null(pending.Value.LastError);
        Assert.Equal("retry_ready", scheduled.Value.State);
        Assert.NotNull(scheduled.Value.NextRetryAt);
        Assert.Equal("dead", manualDead.Value!.State);
        Assert.Equal("bad input", manualDead.Value.LastError);
    }

    [Fact]
    public async Task Bulk_retry_cancel_and_dead_letter_collect_counts_and_errors()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        var first = await Upsert(ledger, "first");
        var second = await Upsert(ledger, "second");
        var third = await Upsert(ledger, "third", stage: "parse");
        var missing = Guid.NewGuid().ToString("N");
        await ledger.DeadLetterItemAsync(first.Value!.Id, "bad");
        await ledger.DeadLetterItemAsync(second.Value!.Id, "bad");

        var retry = await ledger.BulkRetryItemsAsync(new ScriptWorkflowBulkMutationRequest
        {
            ItemIds = [first.Value.Id, missing, second.Value.Id]
        });
        var cancel = await ledger.BulkCancelItemsAsync(new ScriptWorkflowBulkMutationRequest
        {
            ItemIds = [first.Value.Id, second.Value.Id],
            Reason = "manual stop",
            MetadataJson = """{"bulk":true}"""
        });
        var dead = await ledger.BulkDeadLetterItemsAsync(new ScriptWorkflowBulkMutationRequest
        {
            Query = new ScriptWorkflowItemQuery { WorkflowName = "crawl", Stage = "parse" },
            Reason = "not useful"
        });

        Assert.True(retry.Success, retry.Error?.Message);
        Assert.Equal(3, retry.Value!.Requested);
        Assert.Equal(2, retry.Value.Matched);
        Assert.Equal(2, retry.Value.Succeeded);
        Assert.Equal(1, retry.Value.Failed);
        Assert.Contains(retry.Value.Errors, error => error.ItemId == missing);
        Assert.Equal("cancelled", cancel.Value!.Items[0].State);
        Assert.Equal("manual stop", cancel.Value.Items[0].LastError);
        Assert.Equal("""{"bulk":true}""", cancel.Value.Items[0].MetadataJson);
        Assert.Single(dead.Value!.Items);
        Assert.Equal(third.Value!.Id, dead.Value.Items[0].Id);
        Assert.Equal("dead", dead.Value.Items[0].State);
    }

    [Fact]
    public async Task Schema_validator_allows_blocks_and_reports_invalid_schemas()
    {
        var validator = ScriptWorkflowLedgerSchemaValidator.FromJsonByWorkflow(new Dictionary<string, string>
        {
            ["crawl"] = """
            {
              "version": 1,
              "stages": ["fetch"],
              "states": ["pending", "running", "completed"],
              "transitions": [
                { "fromStage": "fetch", "fromState": "pending", "toStage": "fetch", "toState": "running" }
              ]
            }
            """,
            ["broken"] = """{ "version": 2 }"""
        });
        var ledger = new ScriptInMemoryWorkflowLedger(validator);

        var item = await Upsert(ledger, "ok", workflowName: "crawl", stage: "fetch", state: "pending");
        var claimed = await ledger.ClaimItemAsync(
            item.Value!.Id,
            new ScriptWorkflowClaimOptions { LeaseOwner = "worker", LeaseDuration = TimeSpan.FromMinutes(1) });
        var unknownStage = await Upsert(ledger, "bad-stage", workflowName: "crawl", stage: "parse", state: "pending");
        var disallowed = await ledger.CompleteItemAsync(
            claimed.Value!.Id,
            new ScriptWorkflowItemCompleteOptions { LeaseOwner = "worker" });
        var invalidSchema = await Upsert(ledger, "blocked", workflowName: "broken");

        Assert.True(validator.TryGetSchema("crawl", out var schema));
        Assert.Single(schema.Transitions);
        Assert.True(validator.TryGetInvalidSchemaError("broken", out var schemaError));
        Assert.Contains("version must be 1", schemaError);
        Assert.True(claimed.Success, claimed.Error?.Message);
        Assert.False(unknownStage.Success);
        Assert.Equal(ScriptWorkflowLedgerErrorCode.InvalidTransition, unknownStage.Error?.Code);
        Assert.Contains("stage 'parse'", unknownStage.Error!.Message);
        Assert.False(disallowed.Success);
        Assert.Contains("schema rejects complete", disallowed.Error!.Message);
        Assert.False(invalidSchema.Success);
        Assert.Contains("schema is invalid", invalidSchema.Error!.Message);
    }

    [Fact]
    public async Task Public_methods_observe_cancellation_tokens()
    {
        var ledger = new ScriptInMemoryWorkflowLedger();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ledger.StartRunAsync("crawl", cancellationToken: cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ledger.QueryItemsAsync(new ScriptWorkflowItemQuery(), cts.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ledger.BulkRetryItemsAsync(new ScriptWorkflowBulkMutationRequest(), cts.Token));
    }

    private static Task<ScriptWorkflowLedgerResult<ScriptWorkflowItem>> Upsert(
        IScriptWorkflowLedger ledger,
        string itemKey,
        string workflowName = "crawl",
        string? runId = null,
        string stage = "fetch",
        string state = "pending",
        int priority = 0,
        int? maxAttempts = null,
        DateTimeOffset? nextRetryAt = null,
        string? metadataJson = null)
    {
        return ledger.UpsertItemAsync(new ScriptWorkflowItemUpsert
        {
            WorkflowName = workflowName,
            ItemKey = itemKey,
            RunId = runId,
            Stage = stage,
            State = state,
            Priority = priority,
            MaxAttempts = maxAttempts,
            NextRetryAt = nextRetryAt,
            MetadataJson = metadataJson
        });
    }
}
