## 2024-05-23 - Avoid Array Allocation for Threshold Checks
**Learning:** `FindTokenThresholdIndex` was allocating a full `int[]` array for cumulative token counts just to find the first index exceeding a threshold. This is O(N) allocation for a simple O(N) search.
**Action:** Iterate directly and calculate running totals on the fly. Avoid intermediate collection allocations for simple reduction or search operations, especially in hot paths like token estimation.
