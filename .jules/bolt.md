
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2026-07-08 - [Cache collections derived from dictionaries in constructor]
**Learning:** Returning dynamically generated collections using `.Select().ToList()` or `.Values.ToList()` from a parameterless method (e.g., `ToToolDefinitions()`) for internal stable state creates hidden O(N) list allocations and garbage collection on every call in hot paths like the agent loop.
**Action:** In C# performance-critical hot paths, cache stable collections generated from internal state during construction (e.g., storing an `IReadOnlyList<T>`) rather than re-evaluating LINQ expressions in properties or parameterless methods. Ensure implicit deduplication logic (e.g., overwriting duplicate keys) is preserved by building the cached list after the dictionary is fully populated, rather than during the initial enumeration.
