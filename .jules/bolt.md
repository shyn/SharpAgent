
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing .ToList() allocations when rebuilding context
**Learning:** Returning `IReadOnlyList<T>` from frequently invoked builder methods like `SessionManager.RebuildContext()` leads to redundant `.ToList()` calls at call sites (e.g., in `AgentSession.PromptCoreAsync` and `ContinueAsync`) where a mutable `List<T>` is needed. This causes an O(N) array allocation per turn for no reason.
**Action:** Changed the return type of `RebuildContext()` from `IReadOnlyList<LlmMessage>` to `List<LlmMessage>` because it already builds and returns a new `List<T>` internally, eliminating the downstream need for `.ToList()` copying.
