
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2025-03-04 - Eliminate recurring LINQ allocations in ToolRuntime by caching ToolDefinitions
**Learning:** In C# hot paths, executing LINQ methods like `.Select().ToList()` iteratively on properties or helper methods (such as `ToToolDefinitions()`) introduces unnecessary and repeating object and array allocations. These hidden allocations degrade performance, especially when building objects like tool definitions multiple times from an internally stable collection (e.g. `_toolsByName`).
**Action:** Caching stable collections that are derived from dictionaries during construction (e.g. into `IReadOnlyList<T>`) eliminates hidden LINQ allocations on the hot path. Be sure to instantiate lists with `Capacity` when the exact count is known, and preserve any implicit deduplication that happens during dictionary building by waiting until the dictionary is fully populated before materializing the cached list.
