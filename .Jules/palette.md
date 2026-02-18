## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2026-02-18 - Standard Chat Input
**Learning:** Users expect Enter to send and Shift+Enter for new line in chat interfaces. Deviating creates friction.
**Action:** Default to Enter=Send, Shift+Enter=Newline for chat inputs.
