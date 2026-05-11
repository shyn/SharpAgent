
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2023-10-27 - [Optimize .ToList() allocations in CompactionService]
**Learning:** C# LINQ `.ToList()` often obscures N allocations when used with `.Take()`, `.Skip()`, and `.Select()`. Re-evaluating lists using these constructs in memory-sensitive hot paths like Compaction causes hidden garbage collection pressure. Pre-sizing lists and utilizing `.GetRange()` or manual `for` loops helps mitigate these redundant allocations. Avoid replacing `.ToList()` with `new List<T>(collection)` as a micro-optimization for iterables without testing, but manually adding range values based on explicit lists provides real optimization.
**Action:** When working on C# hot paths, explicitly pre-size lists with known capacities and utilize primitive iteration (`for`) or native list operations (`.ConvertAll()`, `.GetRange()`, `.AddRange()`) instead of LINQ composition.
