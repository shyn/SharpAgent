
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing LINQ allocations (.Select().ToList()) in LLM providers
**Learning:** Detected a systemic performance pattern in `Sharp.AI` providers (`AnthropicLlmProvider`, `OpenAiLlmProvider`, `OpenAiResponsesLlmProvider`) where `.Select().ToList()` was chained repeatedly when converting internal objects to provider-specific request schemas. These transformations occur on every LLM loop iteration, accumulating GC pressure and degrading performance in high-throughput hot paths.
**Action:** Replaced `.Select().ToList()` and `.Where().Select().ToList()` chains with manual, zero-allocation `foreach` loop iteration over pre-sized `List<T>` buffers, preventing intermediate GC allocations. Used `messages.Capacity = messages.Count + normalizedMessages.Count;` for collection `AddRange` operations where feasible.
