## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2026-02-19 - Standardizing Chat Input Shortcuts
**Learning:** Chat inputs should default to "Enter to send" to match widespread user mental models from other messaging apps (Slack, Discord, ChatGPT).
**Action:** When implementing chat interfaces, ensure `Enter` sends the message and `Shift+Enter` inserts a newline, overriding default TextBox behavior if necessary.
