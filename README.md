# SharpAgent

SharpAgent is a **library-first coding agent framework** in C#/.NET 10, with a thin CLI host for end-to-end testing.

This repository intentionally removed application entrypoints (Console/API/WinForms) and focuses on:

- `Sharp.AI`: provider abstraction, message blocks, streaming events, OpenAI/Anthropic adapters.
- `Sharp.Core`: session-driven agent runtime, JSONL tree session store, and coding tools (`read/write/edit/bash/grep/find/ls`).
- `Sharp.Cli`: lightweight host over `Sharp.Core` (`run`/`repl`/`models`) for manual validation.
- `Sharp.Core.Tests`: unit and integration tests.

## Repository Layout

```text
SharpAgent/
├── Sharp.AI/
├── Sharp.Cli/
├── Sharp.Core/
├── Sharp.Core.Tests/
├── docs/
└── SharpAgent.sln
```

## Key Design Decisions

- **Library-first, host-second**: core logic stays in `Sharp.Core`; `Sharp.Cli` is intentionally thin.
- **Session model is JSONL tree-based** (`id` + `parentId`), enabling branch rebuild and deterministic context recovery.
- **Tool interface is structured** (JSON arguments + structured content output), no longer plain string-in/string-out.
- **Provider logic is isolated in `Sharp.AI`**; `Sharp.Core` does not depend on provider-specific wire formats.

## Build & Test

```bash
dotnet build SharpAgent.sln
dotnet test SharpAgent.sln
```

## Minimal Library Usage

```csharp
using Sharp.AI;
using Sharp.Core;
using Sharp.Core.Configuration;

var configService = AgentConfigurationService.LoadFromFile(
    AgentConfigurationService.DefaultConfigPath());

var runtimeOptions = configService.BuildRuntimeOptions(
    modelString: "openai/gpt-4o-mini",
    thinkingLevel: ThinkingLevel.Low);

using var session = await AgentSession.CreateAsync(runtimeOptions);

await foreach (var evt in session.PromptAsync("Read README.md and summarize the architecture."))
{
    if (evt is AgentTextDeltaEvent delta)
        Console.Write(delta.Delta);
}
```

## Minimal CLI Usage

```bash
# show help
dotnet run --project Sharp.Cli -- --help

# initialize config template
dotnet run --project Sharp.Cli -- config init

# validate config
dotnet run --project Sharp.Cli -- config validate

# validate config with machine-readable output
dotnet run --project Sharp.Cli -- config validate --json

# list configured models
dotnet run --project Sharp.Cli -- models

# run one prompt
dotnet run --project Sharp.Cli -- run "Read README.md and summarize the architecture."

# interactive mode
dotnet run --project Sharp.Cli -- repl

# runtime streams:
# - assistant text -> stdout
# - event trace (turn/thinking/tool lifecycle) -> stderr
# thinking events depend on provider/model and --thinking level
```

## Configuration Quick Start

- Default config path: `~/Library/Application Support/Sharp/config.json`
- Provider `api` uses pi-style values:
  - `openai-completions`
  - `anthropic-messages`
- `api` is provider-level; model-level `api` is only kept for backward compatibility.
- API key/base URL can be injected by environment variables:
  - `SHARP_<PROVIDER_ID>_API_KEY`, `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_API_KEY`, `<PROVIDER_ID>_BASE_URL`
  - compatibility aliases: `OPENAI_*`, `ANTHROPIC_*`

## Current Scope

Implemented in this phase:

- Provider abstraction and streaming adapters for OpenAI/Anthropic.
- Provider creation registry (`LlmProviderFactory.Register/Unregister`).
- Session-driven loop (`AgentSession` + `AgentLoop` + `ToolRuntime`).
- Session control surface (`ContinueAsync`, `Steer`, `FollowUp`, `Abort`, `WaitForIdleAsync`).
- Tree-structured JSONL session persistence (`SessionManager`).
- Session entries for compaction/branch summary/custom message/label.
- Built-in coding tools: `read`, `write`, `edit`, `bash`, `grep`, `find`, `ls`.
- Thin CLI host with `run`, `repl`, and `models`; REPL local commands include `:continue`, `:reload`, `:diag`, `:tree`, `:fork`, and `:switch`.
- CLI renders core lifecycle events for validation (turn/thinking/tool call/tool execution).
- Core + AI test coverage including an end-to-end session-loop-tool scenario.

Out of scope in this phase:

- TUI/Web UI/WinForms.
- Extension package/version management.
- Session compaction and branch summary UI.

## License

MIT
