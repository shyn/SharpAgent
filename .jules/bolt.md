
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-20 - [Prevent O(N) Array Allocations via RebuildContext List<T> return]
**Learning:** In C# hot paths, returning `IReadOnlyList<T>` from frequently built collection sources (e.g., `SessionManager.RebuildContext()`) forces callers to use `.ToList()` when they need mutability, leading to unnecessary O(N) array allocations.
**Action:** Expose builder methods that internally generate a `List<T>` as returning `List<T>` directly so that downstream consumers can append or modify the list without reallocating, reducing GC overhead.
