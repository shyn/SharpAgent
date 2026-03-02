## 2024-03-24 - [Avoid Unnecessary ToList() calls]
**Learning:** In C#, passing `IEnumerable` or `IReadOnlyList` parameters by calling `.ToList()` creates unnecessary allocations that impact performance, especially when checking token counts frequently in the agent loop.
**Action:** Remove unnecessary `.ToList()` calls when passing collections to methods that already accept an interface like `IReadOnlyList<T>`.
