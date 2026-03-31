
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-05 - Removing redundant .ToList() allocations in session rebuild
**Learning:** Returning `IReadOnlyList<T>` from `SessionManager.RebuildContext()` caused consumers like `AgentSession` to routinely call `.ToList()` to obtain a mutable list for appending messages, resulting in an O(N) array allocation overhead.
**Action:** Changed `SessionManager.RebuildContext()` (which internally builds a new list anyway) to return `List<LlmMessage>` directly, eliminating the redundant array copy in hot paths like `AgentSession.PromptCoreAsync` and `ContinueAsync`.
