## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2025-05-18 - Empty State Pattern
**Learning:** Empty lists (e.g., chat history) can be confusing. Adding a dedicated "Empty State" UI with a greeting and instructions improves user orientation.
**Action:** When implementing lists, always consider the "zero items" state. Use `!HasItems` binding to toggle visibility of a helper panel.
