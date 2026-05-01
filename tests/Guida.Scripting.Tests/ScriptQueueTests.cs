using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptQueueTests
{
    [Fact]
    public void Queue_can_be_registered_and_retrieved_from_host_context()
    {
        var queue = new ScriptInMemoryQueue();
        var context = ScriptHostContext.Empty.WithCapability<IScriptQueue>(queue);

        Assert.True(context.TryGetCapability<IScriptQueue>(out var found));
        Assert.Same(queue, found);
        Assert.Same(queue, context.GetCapability<IScriptQueue>());
    }

    [Fact]
    public void Missing_queue_uses_capability_unavailable_reporting()
    {
        var unavailable = ScriptCapabilityUnavailable.For<IScriptQueue>();
        var result = unavailable.ToExecutionResult();

        Assert.False(ScriptHostContext.Empty.TryGetCapability<IScriptQueue>(out _));
        Assert.Equal(typeof(IScriptQueue).FullName, unavailable.CapabilityName);
        Assert.False(result.Success);
        Assert.Contains(nameof(IScriptQueue), result.Error);
    }

    [Fact]
    public async Task Queue_enqueues_gets_and_lists_item_metadata()
    {
        var queue = new ScriptInMemoryQueue();

        var enqueue = await queue.EnqueueAsync(
            "jobs",
            new byte[] { 1, 2, 3 },
            new ScriptQueueEnqueueOptions
            {
                ItemId = "job-1",
                ContentType = "application/octet-stream"
            });
        var get = await queue.GetAsync("jobs", "job-1");
        var list = await queue.ListAsync("jobs");

        Assert.True(enqueue.Success);
        Assert.True(get.Success);
        Assert.True(list.Success);
        Assert.Equal("job-1", get.Value?.Id);
        Assert.Equal("jobs", get.Value?.QueueName);
        Assert.Equal(new byte[] { 1, 2, 3 }, get.Value?.Payload.ToArray());
        Assert.Equal("application/octet-stream", get.Value?.ContentType);
        Assert.Equal(0, get.Value?.AttemptCount);
        Assert.Null(get.Value?.ClaimedAt);
        Assert.Single(list.Value!);
    }

    [Fact]
    public async Task Queue_defensively_copies_payloads()
    {
        var queue = new ScriptInMemoryQueue();
        var payload = new byte[] { 1, 2, 3 };

        await queue.EnqueueAsync("jobs", payload, new ScriptQueueEnqueueOptions { ItemId = "job-1" });
        payload[0] = 9;

        var first = await queue.GetAsync("jobs", "job-1");
        first.Value!.Payload.ToArray()[1] = 9;
        var second = await queue.GetAsync("jobs", "job-1");

        Assert.Equal(new byte[] { 1, 2, 3 }, first.Value.Payload.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, second.Value?.Payload.ToArray());
    }

    [Fact]
    public async Task Queue_generates_item_ids_when_not_provided()
    {
        var queue = new ScriptInMemoryQueue();

        var first = await queue.EnqueueAsync("jobs", ReadOnlyMemory<byte>.Empty);
        var second = await queue.EnqueueAsync("jobs", ReadOnlyMemory<byte>.Empty);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEmpty(first.Value!.Id);
        Assert.NotEmpty(second.Value!.Id);
        Assert.NotEqual(first.Value.Id, second.Value.Id);
    }

    [Fact]
    public async Task Queue_returns_already_exists_for_duplicate_item_ids()
    {
        var queue = new ScriptInMemoryQueue();

        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "job-1" });
        var result = await queue.EnqueueAsync(
            "jobs",
            new byte[] { 2 },
            new ScriptQueueEnqueueOptions { ItemId = "job-1" });

        Assert.False(result.Success);
        Assert.Equal(ScriptQueueErrorCode.AlreadyExists, result.Error?.Code);
        Assert.Equal("jobs", result.Error?.QueueName);
        Assert.Equal("job-1", result.Error?.ItemId);
    }

    [Fact]
    public async Task Queue_returns_not_found_for_missing_item()
    {
        var queue = new ScriptInMemoryQueue();

        var result = await queue.GetAsync("jobs", "missing");

        Assert.False(result.Success);
        Assert.Equal(ScriptQueueErrorCode.NotFound, result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Queue_returns_invalid_queue_name_for_empty_names(string queueName)
    {
        var queue = new ScriptInMemoryQueue();

        var enqueue = await queue.EnqueueAsync(queueName, ReadOnlyMemory<byte>.Empty);
        var claim = await queue.ClaimAsync(queueName);
        var list = await queue.ListAsync(queueName);

        Assert.Equal(ScriptQueueErrorCode.InvalidQueueName, enqueue.Error?.Code);
        Assert.Equal(ScriptQueueErrorCode.InvalidQueueName, claim.Error?.Code);
        Assert.Equal(ScriptQueueErrorCode.InvalidQueueName, list.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Queue_returns_invalid_item_id_for_empty_ids(string itemId)
    {
        var queue = new ScriptInMemoryQueue();

        var enqueue = await queue.EnqueueAsync(
            "jobs",
            ReadOnlyMemory<byte>.Empty,
            new ScriptQueueEnqueueOptions { ItemId = itemId });
        var get = await queue.GetAsync("jobs", itemId);
        var complete = await queue.CompleteAsync("jobs", itemId);
        var abandon = await queue.AbandonAsync("jobs", itemId);

        Assert.Equal(ScriptQueueErrorCode.InvalidItemId, enqueue.Error?.Code);
        Assert.Equal(ScriptQueueErrorCode.InvalidItemId, get.Error?.Code);
        Assert.Equal(ScriptQueueErrorCode.InvalidItemId, complete.Error?.Code);
        Assert.Equal(ScriptQueueErrorCode.InvalidItemId, abandon.Error?.Code);
    }

    [Fact]
    public async Task Queue_default_claim_strategy_uses_enqueue_order()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "first" });
        await queue.EnqueueAsync("jobs", new byte[] { 2 }, new ScriptQueueEnqueueOptions { ItemId = "second" });

        var result = await queue.ClaimAsync(
            "jobs",
            new ScriptQueueClaimOptions { MaxItemCount = 2 });

        Assert.True(result.Success);
        Assert.Equal(["first", "second"], result.Value!.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Queue_claim_uses_custom_dequeue_strategy()
    {
        var queue = new ScriptInMemoryQueue((items, options) =>
            items.OrderByDescending(item => item.Id, StringComparer.Ordinal).ToArray());
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "a" });
        await queue.EnqueueAsync("jobs", new byte[] { 2 }, new ScriptQueueEnqueueOptions { ItemId = "c" });
        await queue.EnqueueAsync("jobs", new byte[] { 3 }, new ScriptQueueEnqueueOptions { ItemId = "b" });

        var result = await queue.ClaimAsync(
            "jobs",
            new ScriptQueueClaimOptions { MaxItemCount = 2 });

        Assert.True(result.Success);
        Assert.Equal(["c", "b"], result.Value!.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Queue_claim_hides_items_until_abandoned()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "job-1" });

        var first = await queue.ClaimAsync("jobs");
        var hidden = await queue.ClaimAsync("jobs");
        var abandon = await queue.AbandonAsync("jobs", "job-1");
        var second = await queue.ClaimAsync("jobs");

        Assert.Equal("job-1", Assert.Single(first.Value!).Id);
        Assert.Empty(hidden.Value!);
        Assert.True(abandon.Success);
        var reclaimed = Assert.Single(second.Value!);
        Assert.Equal("job-1", reclaimed.Id);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task Queue_claim_hides_items_until_visibility_timeout_expires()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "job-1" });

        var first = await queue.ClaimAsync(
            "jobs",
            new ScriptQueueClaimOptions { VisibilityTimeout = TimeSpan.FromMilliseconds(-1) });
        var second = await queue.ClaimAsync("jobs");

        Assert.Equal("job-1", Assert.Single(first.Value!).Id);
        var reclaimed = Assert.Single(second.Value!);
        Assert.Equal("job-1", reclaimed.Id);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task Queue_complete_removes_item()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "job-1" });

        var complete = await queue.CompleteAsync("jobs", "job-1");
        var get = await queue.GetAsync("jobs", "job-1");

        Assert.True(complete.Success);
        Assert.Equal(ScriptQueueErrorCode.NotFound, get.Error?.Code);
    }

    [Fact]
    public async Task Queue_delayed_items_are_not_claimable_before_available_at()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync(
            "jobs",
            new byte[] { 1 },
            new ScriptQueueEnqueueOptions
            {
                ItemId = "job-1",
                AvailableAt = DateTimeOffset.UtcNow.AddMinutes(1)
            });

        var result = await queue.ClaimAsync("jobs");

        Assert.True(result.Success);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Queue_max_item_count_limits_claims()
    {
        var queue = new ScriptInMemoryQueue();
        await queue.EnqueueAsync("jobs", new byte[] { 1 }, new ScriptQueueEnqueueOptions { ItemId = "first" });
        await queue.EnqueueAsync("jobs", new byte[] { 2 }, new ScriptQueueEnqueueOptions { ItemId = "second" });

        var result = await queue.ClaimAsync(
            "jobs",
            new ScriptQueueClaimOptions { MaxItemCount = 1 });

        Assert.True(result.Success);
        Assert.Equal("first", Assert.Single(result.Value!).Id);
    }

    [Fact]
    public async Task Queue_operations_observe_cancellation()
    {
        var queue = new ScriptInMemoryQueue();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync("jobs", ReadOnlyMemory<byte>.Empty, cancellationToken: cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.ClaimAsync("jobs", cancellationToken: cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.GetAsync("jobs", "job-1", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.ListAsync("jobs", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.CompleteAsync("jobs", "job-1", cancellationTokenSource.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            queue.AbandonAsync("jobs", "job-1", cancellationTokenSource.Token));
    }

    [Fact]
    public void Queue_error_converts_to_failed_execution_result()
    {
        var error = new ScriptQueueError(
            ScriptQueueErrorCode.AccessDenied,
            "jobs",
            "job-1",
            "Queue access was denied.");

        var result = error.ToExecutionResult();

        Assert.False(result.Success);
        Assert.Equal(error.Message, result.Error);
    }
}
