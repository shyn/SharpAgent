## 2025-02-14 - Auto-scroll in Avalonia
**Learning:** Avalonia's `ScrollViewer` does not automatically scroll to bottom when `ItemsControl` content changes. We must subscribe to `CollectionChanged` and invoke `ScrollToEnd()` manually via `Dispatcher.UIThread`.
**Action:** When using `ItemsControl` inside `ScrollViewer` for chat-like interfaces, always implement manual auto-scroll logic in code-behind or a behavior.

## 2025-05-02 - Icon-only Buttons Accessibility in Avalonia
**Learning:** Icon-only buttons in Avalonia (using text characters like ⚙, ☰, ✕) are not accessible by default. Screen readers may read the character literally (e.g., "multiplication sign") or ignore it. `AutomationProperties.Name` is required for a meaningful accessible name, and `ToolTip.Tip` should be added for visual users.
**Action:** Always add `AutomationProperties.Name` and `ToolTip.Tip` to any button that relies solely on an icon or symbol for its label.
