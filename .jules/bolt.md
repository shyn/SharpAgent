
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2024-05-15 - [Avoid redundant ToList allocations by tweaking return type of internal collection source]
**Learning:** Returning `IReadOnlyList<T>` from a frequently accessed and heavily populated internal method like `SessionManager.RebuildContext()` leads to redundant `Array` allocations downstream when callers unnecessarily cast or copy via `.ToList()`. Because `SessionManager` already builds a `List<T>`, we can directly return `List<T>` and avoid redundant object copies in hot paths like `AgentSession`.
**Action:** When a method builds a `List<T>` internally and is called frequently on performance-sensitive paths (e.g., rebuilding state for the agent loop), evaluate whether you can return `List<T>` directly. However, respect external-facing interfaces to prevent breaking changes.

## 2025-03-04 - Removing LINQ allocations in CompactionService
**Learning:** LINQ methods like `.Select().ToList()`, `.Take().ToList()`, and `.Skip().ToList()` cause unnecessary array allocations, resizing, and `IEnumerator` overhead when operating against `IReadOnlyList<T>` or large collections, creating GC pressure in hot paths. Because `.Take()` and `.Skip()` cannot pre-determine the output size from an `IReadOnlyList<T>`, they fall back to dynamic resizing.
**Action:** Replace `Select(x => x.Prop).ToList()` with `ConvertAll(x => x.Prop)` when operating on a backing `List<T>`. Replace `Take(n).ToList()` with `GetRange(0, n)`. For other collection types, prefer manually pre-sizing a `List<T>` with a known capacity and using a `for` loop to eliminate all intermediate overhead.
