## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2025-02-14 - Accessibility for Icon-only Buttons
**Learning:** In Avalonia, `Button` controls with only icon content (e.g., characters like "✕" or "⚙") are inaccessible to screen readers by default. `Content` is not always a suitable accessible name.
**Action:** Always add `AutomationProperties.Name` to icon-only buttons to provide a descriptive label for assistive technologies, and pair it with `ToolTip.Tip` for visual users.
