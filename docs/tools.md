# Tool System

Tools are runtime capabilities executed by `ToolRuntime`.

## Interface

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }
    Task<ToolInvocationResult> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        IProgress<ToolInvocationResult>? progress = null,
        CancellationToken ct = default);
}
```

## Result Model

`ToolInvocationResult` is structured:

- `IsError`
- `Content` (`ContentBlock[]`)
- optional `Details` (`JsonElement`)

The agent converts this into `tool_result` message blocks for the next LLM turn.

## Built-in Tools

- `read`: text/image read with offset/limit and truncation.
- `write`: file write with parent directory creation.
- `edit`: unique text replacement with diff metadata.
- `bash`: shell execution with timeout and output truncation.
- `grep`: regex search in text files.
- `find`: glob-style file matching.
- `ls`: directory listing.

## Design Notes

- All tools resolve relative paths against session working directory.
- Write-oriented tools enforce workspace boundary by default.
- Tool contract is transport-neutral and independent from provider wire format.
- Detailed tool metadata is preserved for future host UIs/log pipelines.
