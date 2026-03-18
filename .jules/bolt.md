
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing LINQ allocations in payload building and compaction
**Learning:** LINQ expressions like `.Select().ToList()` and `.Where().Select().ToList()` in performance-sensitive hot paths (like building request payloads in `AnthropicLlmProvider`, `OpenAiLlmProvider`, `OpenAiResponsesLlmProvider` and parsing chat histories in `CompactionService`) cause unnecessary allocations and memory overhead.
**Action:** Replace these LINQ patterns with pre-sized `List<T>` instantiations and explicit `foreach` loops to prevent excessive garbage collection and intermediate allocations in these critical paths.
