# Tool Output Formatting

In SharpAgent, tool outputs can be complex or overly verbose. To provide a clean and professional user experience, we use a specialized formatting system to render tool inputs and results.

## Overview

The formatting system decouples tools (which return raw strings or JSON) from the display logic (which uses [Spectre.Console](https://spectreconsole.net/) for rich terminal output).

## The Core Design

### 1. `IToolFormatter` Interface

Every formatter implements this interface to control how a tool is presented:

```csharp
public interface IToolFormatter
{
    // Called when the tool execution begins
    void RenderStart(AgentToolCallStartedEvent toolCall);

    // Called when the tool execution completes (success or error)
    void RenderCompleted(AgentToolCallCompletedEvent toolComplete, string toolName, double elapsedMs);
}
```

### 2. Base Implementation

Most formatters inherit from `BaseToolFormatter`, which provides utility methods for:
- **Argument Formatting**: Converting JSON arguments into readable key-value pairs.
- **Output Truncation**: Limiting long outputs with a "more lines" indicator.
- **Common Styling**: Standard icons (✓/✗) and timing information.

## Specialized Formatters

| Formatter | Special Handling |
| :--- | :--- |
| `ReadFileToolFormatter` | Shows summary (lines/bytes) instead of content to avoid "screen flooding". |
| `ListFilesToolFormatter` | Renders a structured directory tree with icons. |
| `BashToolFormatter` | Extracts the exit code and cleans up the raw stdout. |
| `EditFileToolFormatter` | Provides a clear success status for file modifications. |

## How to Add a New Formatter

1.  **Implement**: Create a class in `SharpAgent.Console` inheriting from `BaseToolFormatter`.
2.  **Override**: Customize `RenderStart` or `RenderCompleted` as needed.
3.  **Register**: Add your formatter to the `ToolFormatterDispatcher` constructor:
    ```csharp
    public ToolFormatterDispatcher()
    {
        // ...
        _formatters["my_new_tool"] = new MyNewToolFormatter();
    }
    ```

## Why Decorate Separately?

By keeping formatting logic in the console project and out of the `Sharp.Core` package, we ensure that:
- Core tools remain "logic-only" and easy to test or use in other contexts (e.g., API, GUI).
- We can leverage rich UI libraries like Spectre.Console without adding them as dependencies to the core library.
