## 2024-05-24 - [Compaction Token Calculation Optimization]
**Learning:** `TokenEstimator.EstimateConversationTokens` was being called repeatedly (3x) inside `CompactionService.CompactAsync`, leading to O(N) redundant calculations. Pre-calculating individual message tokens into an array reduced parsing overhead significantly.
**Action:** When working with token limits and cut points, always pre-calculate message tokens once and use array arithmetic for prefix/suffix sums instead of re-estimating tokens for list slices.
