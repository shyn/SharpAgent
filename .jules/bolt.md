
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Eliminate hidden allocations from IReadOnlyList<T>
**Learning:** Returning `IReadOnlyList<T>` from a method like `SessionManager.RebuildContext()` when internally constructing a mutable `List<T>` is an anti-pattern when downstream callers need to add items. It forces callers (like `AgentSession`) to call `.ToList()` to safely mutate the list, creating a redundant array allocation and O(N) copy operations on the hot path.
**Action:** Always return `List<T>` from internal methods that build up collections if the immediate callers need to mutate them, removing the unnecessary interface casting and `.ToList()` allocations.
