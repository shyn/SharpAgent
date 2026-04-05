
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing O(N) array allocation on context rebuild
**Learning:** Returning `IReadOnlyList<T>` from `SessionManager.RebuildContext()` forced `AgentSession` to call `.ToList()` on every turn when appending new messages, causing an unnecessary O(N) array allocation in the critical agent loop.
**Action:** Changed the internal return type of `SessionManager.RebuildContext()` to `List<T>` so that `AgentSession` and other host systems can directly modify the collection or implicitly cast to `IReadOnlyList<T>` without copying memory.
