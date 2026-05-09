
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2026-05-09 - [.NET Enumerable.ToList() optimizations]
**Learning:** Discovered that manually replacing `.ToList()` with `new List<T>()` in modern .NET is a flawed micro-optimization. The underlying `.ToList()` implementation is highly optimized and checks for interfaces like `IIListProvider<T>` and `IReadOnlyCollection<T>`, whereas the `List<T>` constructor only optimizes for older `ICollection<T>` interfaces. Swapping to `new List<T>()` can actually inadvertently *increase* allocations by forcing fallback to enumerator parsing.
**Action:** Do not attempt to replace `.ToList()` with `new List<T>()` as a micro-optimization in C#. To avoid list allocations, focus on caching static collections (like `_toolDefinitions` during initialization) or avoiding LINQ entirely in hot paths.
