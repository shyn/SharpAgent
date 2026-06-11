
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2025-03-04 - Cache static collections derived from dictionaries to avoid hot-path allocations
**Learning:** Found a performance trap in `Sharp.Core.ToolRuntime` where `ToToolDefinitions()` was implemented as a parameterless method executing a LINQ `.Select().ToList()` on `_toolsByName.Values`. Since this method is called continuously on the hot path (e.g., constructing `LlmRequest` in `AgentLoop`), it caused hidden and frequent allocations of lists and objects.
**Action:** In performance-critical hot paths, cache stable collections generated from internal state during construction (like `IReadOnlyList<ToolDefinition> _toolDefinitions`) rather than re-evaluating LINQ expressions in properties or parameterless methods. Ensure the construction of the cached list preserves implicit behaviors (like dictionary key deduplication) by building it after the primary dictionary is populated.
