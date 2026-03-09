
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2023-10-27 - Memoize SessionEntryEnvelope.Payload deserialization
**Learning:** Found an anti-pattern where `SessionEntryEnvelope.Payload` (`JsonElement`) was frequently deserialized via `.Deserialize<T>()` repeatedly during operations like `SessionManager.RebuildContext` and `CompactionService.CompactAsync`. This causes unnecessary GC allocations and CPU overhead in hot paths as session history grows.
**Action:** Implemented a memoization pattern in `SessionEntryEnvelope` using a private `_cachedPayload` field and a `GetPayload<T>()` method. Explicitly overrode `Equals`, `GetHashCode`, and the copy constructor to handle the private field correctly within a C# record type.
