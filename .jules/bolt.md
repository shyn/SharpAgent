
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.
## 2024-05-14 - Replace LINQ `.ToList()` and `.Take()/.Skip()` in core hot paths
**Learning:** Using `Select().ToList()`, `Take()`, and `Skip()` repeatedly in highly-executed framework classes like `CompactionService` introduces hidden allocations and O(N) array copies due to LINQ enumerators. Pre-allocating list sizes and utilizing `ConvertAll()` or explicit loops dramatically reduces GC pressure.
**Action:** When working in hot paths (like compaction and session rebuilding), favor `List<T>.ConvertAll()`, `List<T>.GetRange()`, or a pre-allocated manual `List<T>` loop over lazy LINQ iterations that map to `.ToList()`.
