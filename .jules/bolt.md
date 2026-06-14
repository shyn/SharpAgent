
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2025-03-04 - Cache stable collections to eliminate hidden hot-path allocations
**Learning:** Found an anti-pattern where a parameterless method `ToToolDefinitions()` on `ToolRuntime` re-evaluated a LINQ `.Select().ToList()` expression on an immutable internal dictionary (`_toolsByName`) every time it was called. Since this method is invoked repeatedly in the hot-path (e.g. inside `AgentLoop` when building LLM requests), it resulted in continuous hidden list allocations.
**Action:** When a method returns a stable collection derived from internal state that doesn't change after construction (like tool definitions), build and cache the collection during the class's constructor rather than re-evaluating LINQ expressions. Ensure implicit deduplication logic is preserved by building the cached list after the dictionary is fully populated.
