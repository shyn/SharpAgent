
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-04 - Avoiding eager evaluation in collection operations
**Learning:** Found several places in `CompactionService.cs` where LINQ methods like `Skip` and `Take` were eagerly evaluated using `.ToList()` just to perform further operations like `.FirstOrDefault()`, or pass into methods that could just iterate over an `IEnumerable`. This caused O(N) memory allocations for intermediate lists.
**Action:** Removed redundant `.ToList()` calls after `Skip`, `Take`, and `Select`. Updated method signatures in internal components to accept `IEnumerable<T>` instead of `IReadOnlyList<T>` where random access isn't required.
