using System.Runtime.CompilerServices;
using Sharp.AI;

namespace Sharp.Core.Tests.TestDoubles;

public sealed class RecordingProvider : ILlmProvider
{
    private readonly Queue<Func<LlmRequest, IReadOnlyList<LlmStreamEvent>>> _turns = [];
    private readonly List<LlmRequest> _requests = [];

    public string ProviderId => "recording";

    public IReadOnlyList<LlmRequest> Requests => _requests;

    public void Enqueue(params LlmStreamEvent[] events)
        => Enqueue(_ => events);

    public void Enqueue(Func<LlmRequest, IReadOnlyList<LlmStreamEvent>> eventsFactory)
    {
        ArgumentNullException.ThrowIfNull(eventsFactory);
        _turns.Enqueue(eventsFactory);
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _requests.Add(request);

        if (!_turns.TryDequeue(out var eventsFactory))
            throw new InvalidOperationException("No scripted turn is available");

        foreach (var evt in eventsFactory(request))
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
            await Task.Yield();
        }
    }

    public void Dispose()
    {
    }
}
