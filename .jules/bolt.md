
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing O(N) allocation in SessionManager.RebuildContext
**Learning:** Returning `IReadOnlyList<T>` from a method that inherently builds a new list (like `SessionManager.RebuildContext`) is an anti-pattern when the caller immediately invokes `.ToList()` on the result to modify it. This causes a completely redundant O(N) array allocation and copy on a critical hot path. Additionally, when building lists from a known size source, failing to pre-allocate capacity forces unnecessary internal array resizing during insertions.
**Action:** Changed `SessionManager.RebuildContext` to pre-allocate its list based on `branch.Count` and to return `List<LlmMessage>` directly. Removed redundant `.ToList()` calls in `AgentSession.cs` where the result is consumed and modified, eliminating the redundant allocations.
