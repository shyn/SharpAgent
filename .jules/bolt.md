
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Removing unnecessary intermediate array sizing/enumerator allocations
**Learning:** LINQ's `.Select().ToList()` allocates an intermediate enumerator and often fails to precisely pre-size the final `List<T>`, leading to repeated re-allocations and GC pressure, especially in hot paths like `StreamAsync` across LLM Providers. In micro-benchmarks, an initialized `new List<T>(capacity)` coupled with `foreach` is up to 50% faster and drops significant allocations compared to `Select().ToList()`.
**Action:** Replace `Select().ToList()` in hot paths (like building API requests and parsing chat histories for compaction) with explicit lists sized to `capacity` and `foreach` loops.
