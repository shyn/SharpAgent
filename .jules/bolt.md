
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-05 - Removing LINQ allocations in LLM provider hot paths
**Learning:** Chaining `.Select().ToList()` in the hot path of `ILlmProvider.StreamAsync` causes unnecessary intermediate enumerator and array allocations, which degrades performance under heavy streaming loads when payload builders transform messages and tools.
**Action:** When building provider-specific request payloads (e.g. `AnthropicMessage` or `OpenAiTool`), replace LINQ methods with manual `List<T>` instantiation using `Count` for initial capacity, and populate the list via `foreach` loops to minimize GC pressure.
