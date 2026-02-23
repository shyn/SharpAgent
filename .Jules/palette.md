## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2025-02-17 - Chat Input Key Bindings
**Learning:** `TextBox` with `AcceptsReturn="True"` defaults to Newline on Enter. For chat interfaces, users expect Enter to Send.
**Action:** Override `KeyDown` to handle `Key.Enter` for sending, and check `KeyModifiers.Shift` to allow newline insertion.
