
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing LINQ .Select() allocations in hot paths
**Learning:** Found an anti-pattern where `.Select().ToList()` and `.AddRange(..Select(..))` were used inside core `AgentLoop` operations (like copying `ToolCalls` upon completion) and in `ToolRuntime.ToToolDefinitions()`, creating hidden intermediate enumerators and preventing accurate pre-sizing.
**Action:** Replaced these chains with pre-sized `List<T>` instantiations based on source collection bounds (`.Count`) and simple `foreach` loops to manually `.Add()` items, ensuring zero-allocation loops in highly recurrent request operations.
