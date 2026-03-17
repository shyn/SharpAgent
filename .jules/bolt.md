
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2026-03-17 - Returning List<T> to prevent downstream allocations
**Learning:** Returning `IReadOnlyList<T>` from highly accessed collection-building methods (like `SessionManager.RebuildContext()`) forces callers to allocate new lists via `.ToList()` when they need to append items, which creates O(N) array allocations and excessive garbage collection overhead.
**Action:** Changed methods that intrinsically build and return lists from scratch to return `List<T>` directly, allowing callers (like `AgentSession`) to append items to the existing collection without allocating new arrays.
