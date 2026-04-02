using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Compaction;

namespace Sharp.Core.Sessions;

public sealed class SessionManager
{
    public const int CurrentVersion = 2;

    private const string CompactionSummaryPrefix = "The conversation history before this point was compacted into the following summary:\n\n<summary>\n";
    private const string CompactionSummarySuffix = "\n</summary>";
    private const string BranchSummaryPrefix = "The following is a summary of a branch that this conversation came back from:\n\n<summary>\n";
    private const string BranchSummarySuffix = "\n</summary>";

    private readonly List<SessionEntryEnvelope> _entries = [];
    private readonly Dictionary<string, SessionEntryEnvelope> _entriesById = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    private string? _currentLeafId;

    public string SessionFilePath { get; }
    public SessionHeader Header { get; private set; }

    private SessionManager(string sessionFilePath, SessionHeader header)
    {
        SessionFilePath = sessionFilePath;
        Header = header;
    }

    public string SessionId => Header.SessionId;

    public string? CurrentLeafId => _currentLeafId;

    public IReadOnlyList<SessionEntryEnvelope> Entries => _entries;

    public static async Task<SessionManager> CreateAsync(
        string sessionDirectory,
        string workingDirectory,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(sessionDirectory);

        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId;

        var filePath = Path.Combine(sessionDirectory, $"{effectiveSessionId}.jsonl");

        if (File.Exists(filePath))
            return await LoadAsync(filePath, ct);

        var header = new SessionHeader(
            Type: "session",
            Version: CurrentVersion,
            SessionId: effectiveSessionId,
            WorkingDirectory: Path.GetFullPath(workingDirectory),
            TimestampUtc: DateTimeOffset.UtcNow);

        await using (var writer = new StreamWriter(filePath, append: false))
        {
            var headerJson = JsonSerializer.Serialize(header, JsonDefaults.Options);
            await writer.WriteLineAsync(headerJson);
        }

        return new SessionManager(filePath, header);
    }

    public static async Task<SessionManager> LoadAsync(string sessionFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(sessionFilePath))
            throw new FileNotFoundException($"Session file not found: {sessionFilePath}");

        await using var stream = File.OpenRead(sessionFilePath);
        using var reader = new StreamReader(stream);

        var headerLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidDataException("Session file is empty");

        var parsedHeader = JsonSerializer.Deserialize<SessionHeader>(headerLine, JsonDefaults.Options)
            ?? throw new InvalidDataException("Failed to parse session header");

        var migratedHeader = MigrateHeader(parsedHeader);
        var manager = new SessionManager(sessionFilePath, migratedHeader);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = JsonSerializer.Deserialize<SessionEntryEnvelope>(line, JsonDefaults.Options);
            if (entry == null)
                continue;

            manager._entries.Add(entry);
            manager._entriesById[entry.Id] = entry;
            manager._currentLeafId = entry.Id;
        }

        return manager;
    }

    private static SessionHeader MigrateHeader(SessionHeader header)
    {
        var version = header.Version <= 0 ? 1 : header.Version;
        if (version >= CurrentVersion)
            return header;

        return header with { Version = CurrentVersion };
    }

    public async Task<SessionEntryEnvelope> AppendMessageAsync(LlmMessage message, CancellationToken ct = default)
        => await AppendEntryAsync("message", new MessageEntryPayload(message), ct);

    public async Task<SessionEntryEnvelope> AppendModelChangeAsync(string provider, string modelId, CancellationToken ct = default)
        => await AppendEntryAsync("model_change", new ModelChangeEntryPayload(provider, modelId), ct);

    public async Task<SessionEntryEnvelope> AppendThinkingChangeAsync(ThinkingLevel thinkingLevel, CancellationToken ct = default)
        => await AppendEntryAsync("thinking_change", new ThinkingChangeEntryPayload(thinkingLevel), ct);

    public async Task<SessionEntryEnvelope> AppendMetadataAsync(string key, string value, CancellationToken ct = default)
        => await AppendEntryAsync("metadata", new MetadataEntryPayload(key, value), ct);

    public async Task<SessionEntryEnvelope> AppendCompactionAsync(
        string summary,
        string firstKeptEntryId,
        int tokensBefore,
        JsonElement? details = null,
        bool fromHook = false,
        CancellationToken ct = default)
        => await AppendEntryAsync(
            "compaction",
            new CompactionEntryPayload(summary, firstKeptEntryId, tokensBefore, details, fromHook),
            ct);

    public async Task<SessionEntryEnvelope> AppendBranchSummaryAsync(
        string fromId,
        string summary,
        JsonElement? details = null,
        bool fromHook = false,
        CancellationToken ct = default)
        => await AppendEntryAsync(
            "branch_summary",
            new BranchSummaryEntryPayload(fromId, summary, details, fromHook),
            ct);

    public async Task<SessionEntryEnvelope> AppendCustomMessageAsync(
        string customType,
        string content,
        bool display = true,
        JsonElement? details = null,
        CancellationToken ct = default)
        => await AppendEntryAsync(
            "custom_message",
            new CustomMessageEntryPayload(customType, content, display, details),
            ct);

    public async Task<SessionEntryEnvelope> AppendLabelAsync(
        string targetId,
        string? label,
        CancellationToken ct = default)
        => await AppendEntryAsync("label", new LabelEntryPayload(targetId, label), ct);

    /// <summary>
    /// Applies a compaction result by creating a compaction entry.
    /// The compacted entries remain in the session but are excluded from context reconstruction.
    /// </summary>
    /// <param name="result">The compaction result from CompactionService.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created compaction entry.</returns>
    public async Task<SessionEntryEnvelope> ApplyCompactionAsync(
        CompactionResult result,
        CancellationToken ct = default)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        return await AppendCompactionAsync(
            result.Summary,
            result.FirstKeptEntryId ?? string.Empty,
            result.TokensBefore,
            result.Details,
            result.FromHook,
            ct);
    }

    /// <summary>
    /// Gets all entries that would be compacted given a compaction result.
    /// Useful for displaying what was compacted.
    /// </summary>
    /// <param name="result">The compaction result.</param>
    /// <returns>The list of entries that were compacted.</returns>
    public IReadOnlyList<SessionEntryEnvelope> GetCompactedEntries(CompactionResult result)
    {
        if (result?.CompactedEntryIds == null)
            return [];

        var compacted = new List<SessionEntryEnvelope>();
        foreach (var id in result.CompactedEntryIds)
        {
            if (_entriesById.TryGetValue(id, out var entry))
                compacted.Add(entry);
        }
        return compacted;
    }

    /// <summary>
    /// Gets the current branch entries for compaction analysis.
    /// </summary>
    /// <returns>The entries from root to current leaf.</returns>
    public IReadOnlyList<SessionEntryEnvelope> GetCurrentBranch()
        => GetBranch(_currentLeafId);

    /// <summary>
    /// Checks if a compaction entry exists in the current branch.
    /// </summary>
    /// <returns>True if the conversation has been compacted.</returns>
    public bool HasCompaction()
    {
        var branch = GetBranch(_currentLeafId);
        return branch.Any(e => e.Type == "compaction");
    }

    /// <summary>
    /// Gets the most recent compaction entry in the current branch, if any.
    /// </summary>
    /// <returns>The compaction entry payload, or null if no compaction exists.</returns>
    public CompactionEntryPayload? GetLatestCompaction()
    {
        var branch = GetBranch(_currentLeafId);

        for (var i = branch.Count - 1; i >= 0; i--)
        {
            if (branch[i].Type == "compaction")
            {
                return branch[i].Payload.Deserialize<CompactionEntryPayload>(JsonDefaults.Options);
            }
        }

        return null;
    }

    public void SwitchLeaf(string entryId)
    {
        if (!_entriesById.ContainsKey(entryId))
            throw new InvalidOperationException($"Entry '{entryId}' does not exist in this session");

        _currentLeafId = entryId;
    }

    public IReadOnlyList<SessionEntryEnvelope> GetBranch(string? leafEntryId = null)
    {
        var effectiveLeaf = leafEntryId ?? _currentLeafId;
        if (effectiveLeaf == null)
            return [];

        var branch = new List<SessionEntryEnvelope>();
        var cursor = effectiveLeaf;

        while (cursor != null && _entriesById.TryGetValue(cursor, out var entry))
        {
            branch.Add(entry);
            cursor = entry.ParentId;
        }

        branch.Reverse();
        return branch;
    }

    /// <summary>
    /// Rebuilds the LLM context from the current branch.
    /// Returns a List<LlmMessage> directly instead of an interface to prevent
    /// redundant O(N) array allocations from callers performing downstream LINQ operations.
    /// </summary>
    public List<LlmMessage> RebuildContext(string? leafEntryId = null)
    {
        var branch = GetBranch(leafEntryId);
        var messages = new List<LlmMessage>();

        var compactionIndex = -1;
        CompactionEntryPayload? compactionPayload = null;
        for (var i = 0; i < branch.Count; i++)
        {
            if (branch[i].Type != "compaction")
                continue;

            compactionIndex = i;
            compactionPayload = branch[i].Payload.Deserialize<CompactionEntryPayload>(JsonDefaults.Options);
        }

        if (compactionIndex >= 0 && compactionPayload != null)
        {
            messages.Add(LlmMessage.UserText(CompactionSummaryPrefix + compactionPayload.Summary + CompactionSummarySuffix));

            var firstKeptIndex = -1;
            for (var i = 0; i < branch.Count; i++)
            {
                if (!string.Equals(branch[i].Id, compactionPayload.FirstKeptEntryId, StringComparison.Ordinal))
                    continue;

                firstKeptIndex = i;
                break;
            }
            if (firstKeptIndex >= 0 && firstKeptIndex < compactionIndex)
            {
                for (var i = firstKeptIndex; i < compactionIndex; i++)
                    AppendContextEntry(branch[i], messages);
            }

            for (var i = compactionIndex + 1; i < branch.Count; i++)
                AppendContextEntry(branch[i], messages);

            return messages;
        }

        foreach (var entry in branch)
            AppendContextEntry(entry, messages);

        return messages;
    }

    private static void AppendContextEntry(SessionEntryEnvelope entry, List<LlmMessage> messages)
    {
        switch (entry.Type)
        {
            case "message":
            {
                var payload = entry.Payload.Deserialize<MessageEntryPayload>(JsonDefaults.Options);
                if (payload?.Message != null)
                    messages.Add(payload.Message);
                break;
            }
            case "custom_message":
            {
                var payload = entry.Payload.Deserialize<CustomMessageEntryPayload>(JsonDefaults.Options);
                if (!string.IsNullOrWhiteSpace(payload?.Content))
                    messages.Add(LlmMessage.UserText(payload.Content));
                break;
            }
            case "branch_summary":
            {
                var payload = entry.Payload.Deserialize<BranchSummaryEntryPayload>(JsonDefaults.Options);
                if (!string.IsNullOrWhiteSpace(payload?.Summary))
                    messages.Add(LlmMessage.UserText(BranchSummaryPrefix + payload.Summary + BranchSummarySuffix));
                break;
            }
        }
    }

    public async Task<SessionEntryEnvelope> AppendEntryAsync<TPayload>(
        string type,
        TPayload payload,
        CancellationToken ct = default)
    {
        await _appendLock.WaitAsync(ct);
        try
        {
            var entry = new SessionEntryEnvelope(
                Type: type,
                Id: Guid.NewGuid().ToString("N")[..8],
                ParentId: _currentLeafId,
                TimestampUtc: DateTimeOffset.UtcNow,
                Payload: JsonSerializer.SerializeToElement(payload, JsonDefaults.Options));

            await using var writer = new StreamWriter(SessionFilePath, append: true);
            var json = JsonSerializer.Serialize(entry, JsonDefaults.Options);
            await writer.WriteLineAsync(json);

            _entries.Add(entry);
            _entriesById[entry.Id] = entry;
            _currentLeafId = entry.Id;

            return entry;
        }
        finally
        {
            _appendLock.Release();
        }
    }
}
