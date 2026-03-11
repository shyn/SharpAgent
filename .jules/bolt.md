
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing LINQ .Select().ToList() in Compaction Hot Path
**Learning:** Found an anti-pattern in `CompactionService.cs` where `conversationEntries.Select(e => e.Message).ToList()` was creating excessive intermediate garbage collection pressure and multiple O(N) array instantiations during the LLM conversation compaction cycle. The LINQ extension methods create extra enumerable state machines on very hot paths.
**Action:** Replaced `.Select(e => e.Message).ToList()` with a manually allocated `List<LlmMessage>` loop (i.e. `new List<LlmMessage>(conversationEntries.Count)`) to directly construct the list, eliminating the LINQ overhead and extra allocations.
