# Bolt's Journal

## 2026-02-13 - [Session Context Reconstruction Optimization]
**Learning:** `SessionManager.GetBranch` allocates a full list of history (O(N)) on every turn to rebuild context or check compaction, even when only recent messages are needed. This is a bottleneck for long sessions.
**Action:** Use lazy traversal (IEnumerable) from leaf to root to find compaction points and stop early, avoiding full history allocation.
