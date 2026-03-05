
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-05 - Caching deserialized JSON payloads in SessionEntryEnvelope
**Learning:** Found a performance bottleneck where `JsonElement.Deserialize<T>()` was repeatedly called for `SessionEntryEnvelope.Payload` in tight loops and properties (e.g., UI rendering, compaction, and session management). Since JSON deserialization involves memory allocation and reflection, this was causing unnecessary overhead.
**Action:** Implemented a memoization pattern in `SessionEntryEnvelope` by adding a `GetPayload<T>()` method that caches the deserialized object, and updated the codebase to use `GetPayload<T>()` instead of calling `Payload.Deserialize<T>()` directly.
