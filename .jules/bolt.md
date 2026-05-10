
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2024-05-30 - Double Enumeration in Dependency Injection Constructors
**Learning:** When aggressively replacing LINQ allocations with cached structures (like creating a `List<T>` from `IEnumerable<T>`), directly iterating the passed-in `IEnumerable` parameter to build the cache can lead to the anti-pattern of double-enumeration if that enumerable is used elsewhere in the same constructor. This was discovered when optimizing `ToolRuntime.cs` where `tools` was enumerated first to build `_toolsByName` and second to build `_toolDefinitions`.
**Action:** Always rebuild new optimized collections from already-materialized local collections (e.g., `_toolsByName.Values`) rather than re-enumerating the raw dependency-injected `IEnumerable`.
