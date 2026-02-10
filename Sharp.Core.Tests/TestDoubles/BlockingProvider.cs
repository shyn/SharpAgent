using System.Runtime.CompilerServices;
using Sharp.AI;

namespace Sharp.Core.Tests.TestDoubles;

public sealed class BlockingProvider : ILlmProvider
{
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ProviderId => "blocking";

    public Task WaitForStartedAsync(CancellationToken ct = default)
        => _started.Task.WaitAsync(ct);

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _started.TrySetResult(true);

        var completion = new TaskCompletionSource<LlmStreamEvent?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = ct.Register(() =>
            completion.TrySetResult(new LlmErrorEvent(
                "blocked stream aborted",
                LlmErrorCategory.Aborted,
                Retryable: true)));

        var evt = await completion.Task;
        if (evt != null)
            yield return evt;
    }

    public void Dispose()
    {
    }
}
