using FluentAssertions;
using OpenFarm.Application.AuditLog;

namespace OpenFarm.Application.Tests.AuditLog;

public class AuditContextScopeTests
{
    [Fact]
    public async Task ParallelOperations_KeepTheirOwnAttribution_AndRestoreTheParent()
    {
        using var parent = new AuditContextScope(new(null, null, null, "Parent", "parent"));
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string?> Operation(string id)
        {
            using var child = new AuditContextScope(new(null, null, null, id, id));
            if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
            await bothEntered.Task;
            return AuditContextScope.Current?.CorrelationId;
        }
        var results = await Task.WhenAll(Operation("first"), Operation("second"));
        results.Should().Equal("first", "second");
        AuditContextScope.Current!.CorrelationId.Should().Be("parent");
    }

    [Fact]
    public async Task FailedOperation_DoesNotLeakAttributionIntoTheNextOperation()
    {
        async Task FailAsync()
        {
            using var scope = new AuditContextScope(new(null, null, null, "Failed", "failed"));
            await Task.Yield();
            throw new InvalidOperationException("Failed operation");
        }
        Func<Task> fail = FailAsync;
        await fail.Should().ThrowAsync<InvalidOperationException>();
        AuditContextScope.Current.Should().BeNull();
    }
}
