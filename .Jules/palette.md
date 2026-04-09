## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2025-02-14 - Chat Input UX
**Learning:** Avalonia's `TextBox` with `AcceptsReturn="True"` defaults to "Enter for Newline" which conflicts with standard Chat UX ("Enter to Send").
**Action:** Always override `KeyDown` for Chat inputs to implement "Enter to Send, Shift+Enter for Newline" and update the placeholder/watermark accordingly.
