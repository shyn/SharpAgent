## 2024-05-22 - [Optimizing Token Estimation]
**Learning:** `Math.Ceiling(len / 4.0)` is significantly slower than integer arithmetic `(len + 3) >> 2` in .NET 10, even for simple cases. Floating point operations and function call overhead add up in hot paths.
**Action:** Use integer arithmetic and bit shifting for division by powers of 2 in hot paths.

## 2024-05-22 - [Allocation in Hot Loops]
**Learning:** Allocating an array just to find a threshold index (e.g., `CalculateCumulativeTokens` used only for `FindTokenThresholdIndex`) is a wasteful O(N) allocation.
**Action:** Use on-the-fly accumulation in loops instead of intermediate arrays for simple search/threshold logic.
