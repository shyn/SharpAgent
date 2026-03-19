
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.
## 2024-05-18 - LINQ Allocation in Request Payload Hot Paths
**Learning:** In LLM providers (`AnthropicLlmProvider`, `OpenAiLlmProvider`) and `CompactionService`, `.Select().ToList()` chains inside request payload building cause significant intermediate array allocations and GC pressure.
**Action:** Replaced `.Select()` with manually sized `List<T>` initializations and explicit `foreach` loops to minimize memory garbage on hot paths.
