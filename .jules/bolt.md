
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Eliminate Downstream Collections Copies in Core Hot Paths
**Learning:** Found an anti-pattern where frequent collection operations (like `.Skip().ToList()`, `.Take().ToList()`, and `.Select().ToList()`) created intermediate array copies unnecessarily, placing significant load on the GC in hot path operations like context rebuilding and conversation compaction. Additionally, returning `IReadOnlyList<T>` from `RebuildContext` effectively forced callers to repeatedly invoke `.ToList()`.
**Action:** Changed the core signature for `SessionManager.RebuildContext()` to directly return `List<LlmMessage>`, replacing upstream `.ToList()` calls, and refactored inner operations inside `CompactionService` to use pre-allocated Lists (`Capacity = ...`) with standard `for` and `foreach` loops to minimize reallocation overhead.
