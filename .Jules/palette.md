## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2026-02-22 - Chat Input Key Bindings
**Learning:** Default `AcceptsReturn="True"` in Avalonia TextBoxes creates a disconnect for chat UIs where users expect "Enter to Send".
**Action:** Always intercept `Key.Enter` in chat inputs to send, and explicitly check for `KeyModifiers.Shift` to allow newlines (by returning early). Update watermark text to match.
