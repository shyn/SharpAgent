
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2024-06-18 - Caching derived collections in constructors to avoid hidden hot-path allocations
**Learning:** Returning dynamically built collections via LINQ expressions (e.g., `.Select().ToList()`) inside a parameterless property or method like `ToToolDefinitions()` forces an array reallocation and iteration every time it's called. In highly iterative hot paths like `AgentLoop`, this creates a hidden O(N) memory allocation bottleneck.
**Action:** When a stable collection is derived from dictionary values, cache the final list explicitly in the constructor. Furthermore, if the dictionary's size is known, initialize it with a predefined capacity (e.g., using `TryGetNonEnumeratedCount`) to prevent internal buffer resizing during initialization. Ensure that when building the cached list, you do it *after* the dictionary is fully populated to preserve any implicit key deduplication logic.
