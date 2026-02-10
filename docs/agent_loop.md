# Agent Loop

Sharp.Core uses a session-driven ReAct loop.

## Runtime Entry

- `AgentSession.PromptAsync(...)` is the entrypoint.
- A user message is persisted to `SessionManager` first.
- `AgentLoop` then iterates until completion or max turns.

## Turn Sequence

1. Build `LlmRequest` from:
   - model descriptor
   - system prompt
   - rebuilt branch context (`SessionManager.RebuildContext()`)
   - tool definitions (`ToolRuntime.ToToolDefinitions()`)
2. Stream provider events (`ILlmProvider.StreamAsync`).
3. Assemble assistant message (`text` / `thinking` / `tool_call` blocks).
4. Persist assistant message.
5. If there are tool calls:
   - execute each call through `ToolRuntime`
   - emit tool execution partial updates when available
   - check steering queue after each tool result
   - persist tool result message
   - continue to next turn
6. If there are no tool calls:
   - check follow-up queue
   - emit `AgentCompletedEvent`
   - stop.

## Safety Boundaries

- `MaxTurns` prevents infinite loops.
- Tool errors are converted into tool-result messages with `IsError=true`.
- Conversation reconstruction is deterministic because branch context comes from JSONL entries.
- `AgentSession` supports `ContinueAsync`, `Steer(...)`, `FollowUp(...)`, `Abort()`, and `WaitForIdleAsync()`.
