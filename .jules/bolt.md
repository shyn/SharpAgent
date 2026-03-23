
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-05 - Removing LINQ allocations when constructing LLM requests
**Learning:** LINQ methods (`Select`, `Where`, `OfType`) in high-frequency methods like `StreamAsync` within LLM providers (`OpenAiLlmProvider` and `AnthropicLlmProvider`) cause unnecessary array allocations and garbage collection overhead. This is a common bottleneck when transforming lists of `LlmMessage` and `ToolDefinition`s to provider-specific request formats before making API calls.
**Action:** Replace `Select().ToList()` chains and LINQ `OfType` filters with manually pre-allocated `List<T>` instances initialized with explicit capacity (`new List<T>(count)`) and use standard `foreach` loops with `is` pattern matching.
