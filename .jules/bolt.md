
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-25 - Removing redundant .ToList() from RebuildContext
**Learning:** Found an anti-pattern where a frequently built collection source (`SessionManager.RebuildContext()`) was returning `IReadOnlyList<T>`, forcing callers in performance-critical hot paths (like `AgentSession`) to call `.ToList()` to mutate it, leading to redundant O(N) array allocations.
**Action:** Changed the return type of `SessionManager.RebuildContext()` directly to `List<T>` to eliminate downstream list allocations before mutation.
