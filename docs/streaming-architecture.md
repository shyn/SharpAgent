# Streaming Architecture

Streaming is modeled in two layers.

## 1) Provider Layer (`Sharp.AI`)

`ILlmProvider.StreamAsync(LlmRequest)` emits `LlmStreamEvent`:

- thinking start/delta/end
- text delta
- tool use start/args delta/end
- completed/error

Provider adapters (OpenAI/Anthropic) normalize protocol-specific SSE into this shared event model.

## 2) Agent Layer (`Sharp.Core`)

`AgentLoop.RunAsync(...)` converts provider events into `AgentEvent` and performs tool dispatch.

Agent events include:

- loop lifecycle (`AgentStartedEvent`, `AgentCompletedEvent`, `AgentErrorEvent`)
- streaming deltas (`AgentTextDeltaEvent`, `AgentThinkingDeltaEvent`)
- tool lifecycle (`AgentToolUse*`, `AgentToolExecution*`)

## Persistence Interaction

`SessionManager` persists full turn artifacts as JSONL entries. Rebuilding context is independent from stream transport.

## Why This Split

- provider changes do not affect loop semantics
- loop tests can use scripted providers
- deterministic replay comes from stored messages, not from event bus state
