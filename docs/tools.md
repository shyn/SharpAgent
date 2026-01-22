# Tool System

Tools grant SharpAgent the ability to interact with the real world.

## Implementing ITool

Every tool must implement the `ITool` interface:

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<string> ExecuteAsync(string input, CancellationToken ct = default);
}
```

- **`Name`**: Unique identifier (e.g., `read_file`).
- **`Description`**: Instructs the LLM on when and how to use the tool.
- **`ParametersSchema`**: A JSON Schema object explaining the expected arguments.
- **`ExecuteAsync`**: The logic that runs on the user's machine.

## Built-in Tools

| Tool | Purpose |
| :--- | :--- |
| `BashTool` | Executes arbitrary shell commands. |
| `EditFileTool` | Modifies file content using search/replace patterns. |
| `ReadFileTool` | Reads the content of a specific file. |
| `ListFilesTool` | Lists files in a directory. |
| `GrepTool` | Searches for patterns within files. |
| `GlobTool`| Finds files using glob patterns. |
| `CalculatorTool` | Performs mathematical calculations. |

## Safety
By default, the agent has the same permissions as the user running the process. Using `BashTool` or `EditFileTool` should be done with caution in untrusted environments.
