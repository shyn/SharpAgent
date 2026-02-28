## 2026-02-28 - Avoiding Unnecessary Allocations with IReadOnlyList
**Learning:** Found redundant `.ToList()` calls converting `IReadOnlyList<LlmMessage>` instances just to pass them to `TokenEstimator.EstimateConversationTokens`, causing O(N) memory allocation per token estimation loop.
**Action:** When passing collections to read-only analytical functions (like token estimators), pass the `IReadOnlyList` or `IEnumerable` directly instead of materializing it into a new list. This avoids memory overhead on hot paths.
