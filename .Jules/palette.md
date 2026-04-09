## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2026-02-16 - Chat Input Focus
**Learning:** In chat interfaces, users expect focus to return to the input field immediately after sending a message, even while the bot is processing.
**Action:** Implement focus restoration in the View code-behind by listening to ViewModel state changes (e.g., `IsProcessing` becoming `true`) rather than relying on command completion.
