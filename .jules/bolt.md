
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.
## 2025-03-04 - Removing redundant .ToList() allocations in RebuildContext
**Learning:** Returning `IReadOnlyList<T>` from sources that dynamically build collections (like `SessionManager.RebuildContext()`) forces downstream callers that need a mutable list to call `.ToList()`, leading to O(N) memory allocations and GC pressure in hot paths.
**Action:** Changed `SessionManager.RebuildContext()` to return `List<LlmMessage>` directly instead of `IReadOnlyList<LlmMessage>`, allowing downstream consumers (like `AgentSession`) to mutate the resulting collection directly without calling `.ToList()`.
