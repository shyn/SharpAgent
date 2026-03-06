
## 2025-03-04 - Removing .ToList() allocations in token estimation
**Learning:** Found an anti-pattern where `.ToList()` was frequently called before passing `IReadOnlyList<LlmMessage>` into `TokenEstimator.EstimateConversationTokens`, causing unnecessary list allocations and copying, especially during looping and slicing operations (e.g. `Take()`) in hot paths like `AgentLoop` and `CompactionService`.
**Action:** Overloaded `TokenEstimator.EstimateConversationTokens` and `TokenEstimator.CalculateCumulativeTokens` to accept `IEnumerable<LlmMessage>` instead of `IReadOnlyList<LlmMessage>`, preventing the need for `.ToList()` and eliminating the memory allocation bottleneck.

## 2025-03-05 - Avoid changing record fields without custom Equals/GetHashCode
**Learning:** By adding a private memoization field (`_cachedPayload`) to a C# `record` without overriding `Equals` and `GetHashCode`, the compiler's auto-generated equality methods will include the private field. This mutating field breaks the record's immutability and value-equality contracts, causing bugs if the record is used in hash sets or dictionaries.
**Action:** When adding memoization to a `record`, explicitly override `Equals` and `GetHashCode` to exclude the memoization field, or change the type to a regular `class`.

## 2025-03-05 - Records copy constructor and memoization
**Learning:** C# `record` types automatically generate a copy constructor for `with` expressions that copies all fields, including private ones. If you add a private memoization field, the copy constructor will carry over the cached value, causing stale data if the record is mutated via `with`. Additionally, `JsonElement.ToString()` allocates a string and should not be used in `Equals` or `GetHashCode`.
**Action:** Always provide a custom copy constructor `private RecordName(RecordName original)` to clear memoization fields. For `JsonElement`, use `EqualityComparer<JsonElement>.Default.Equals` and `.GetHashCode()`.
