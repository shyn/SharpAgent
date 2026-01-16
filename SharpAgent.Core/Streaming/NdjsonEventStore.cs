using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SharpAgent.Core.Streaming;

public sealed class NdjsonEventStore : IEventStore
{
    private readonly string _baseDir;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _writeLock = new();

    public NdjsonEventStore(string baseDir, JsonSerializerOptions? jsonOptions = null)
    {
        _baseDir = baseDir;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Directory.CreateDirectory(_baseDir);
    }

    private string PathFor(StreamId id) => Path.Combine(_baseDir, $"{id.Value}.ndjson");

    public async ValueTask AppendAsync(AgentEventEnvelope evt, CancellationToken ct = default)
    {
        var path = PathFor(evt.StreamId);
        var line = JsonSerializer.Serialize(evt, _jsonOptions) + "\n";
        
        lock (_writeLock)
        {
            File.AppendAllText(path, line);
        }
        
        await ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEventEnvelope> ReadFromAsync(
        StreamId streamId, 
        long startSeq,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var path = PathFor(streamId);
        if (!File.Exists(path)) yield break;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);

        string? line;
        while ((line = await sr.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var evt = JsonSerializer.Deserialize<AgentEventEnvelope>(line, _jsonOptions)!;
            if (evt.Seq >= startSeq) yield return evt;
        }
    }

    public async ValueTask<long?> TryGetLastSeqAsync(StreamId streamId, CancellationToken ct = default)
    {
        var path = PathFor(streamId);
        if (!File.Exists(path)) return null;

        long? lastSeq = null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);

        string? line;
        while ((line = await sr.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var evt = JsonSerializer.Deserialize<AgentEventEnvelope>(line, _jsonOptions)!;
            lastSeq = evt.Seq;
        }

        return lastSeq;
    }
}
