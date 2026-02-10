# Architecture Overview

SharpAgent is split into two runtime libraries plus tests.

## Components

### 1. `Sharp.AI`

Responsibilities:

- Unified model/message abstractions.
- Structured content blocks (`text`, `image`, `thinking`, `tool_call`, `tool_result`).
- Streaming event model for provider adapters.
- Provider adapters:
  - OpenAI Chat Completions (`OpenAiLlmProvider`)
  - OpenAI Responses (`OpenAiResponsesLlmProvider`)
  - Anthropic Messages (`AnthropicLlmProvider`)

Core interfaces:

- `ILlmProvider`
- `LlmRequest`
- `LlmStreamEvent`
- `ToolDefinition`
- `LlmMessage`

### 2. `Sharp.Core`

Responsibilities:

- Session-driven orchestration (`AgentSession`).
- Multi-turn agent loop with tool execution (`AgentLoop`).
- Session control operations (`ContinueAsync`, steering/follow-up queue, abort/wait idle).
- Tool runtime dispatch (`ToolRuntime`).
- Tree-based JSONL persistence (`SessionManager`).
- Extension runtime hooks (session/input/context/tool lifecycle events).
- Compaction primitives (`CompactionService`, compaction session entries).
- Built-in coding tools: `read`, `write`, `edit`, `bash`, `grep`, `find`, `ls`.
- Configuration utilities (`AgentConfigurationService`).

Core interfaces/classes:

- `IAgentTool`
- `ToolInvocationResult`
- `AgentEvent`
- `SessionManager`

### 3. `Sharp.Core.Tests`

Responsibilities:

- Unit tests for tool/runtime/config/session behavior.
- Provider mapping and streaming assembly tests.
- End-to-end library integration tests (`session + loop + tool call`).

## High-Level Flow

```mermaid
graph TD
    App["Library Consumer"] --> Session["AgentSession"]
    Session --> Loop["AgentLoop"]
    Loop --> Runtime["ToolRuntime"]
    Loop --> Provider["ILlmProvider"]
    Provider --> OpenAICompletions["OpenAiLlmProvider"]
    Provider --> OpenAIResponses["OpenAiResponsesLlmProvider"]
    Provider --> Anthropic["AnthropicLlmProvider"]
    Session --> Store["SessionManager JSONL Tree"]
```

## Session Storage

- One `.jsonl` file per session.
- First line: `session` header.
- Subsequent lines: entries with `id`, `parentId`, `type`, `payload`.
- Context rebuild supports `message`, `custom_message`, `branch_summary`, and `compaction`.
- Compaction anchors restoration with `firstKeptEntryId` (entry-id boundary).

## Non-Goals in This Phase

- No interactive shell, no web API, no desktop UI.
- No extension isolation/unload via `AssemblyLoadContext` yet.
- No automatic compaction wiring in `AgentSession` loop yet.
- No UI-level compaction workflow yet (entries and context rebuild hooks exist in core storage).
