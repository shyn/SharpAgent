# SharpAgent

SharpAgent is a **library-first coding agent framework** in C#/.NET 10, with a thin CLI host for end-to-end testing.

This repository intentionally removed application entrypoints (Console/API/WinForms) and focuses on:

- `Sharp.AI`: provider abstraction, message blocks, streaming events, OpenAI/Anthropic adapters.
- `Sharp.Core`: session-driven agent runtime, JSONL tree session store, and coding tools (`read`, `write`, `edit`, `bash`).
- `Sharp.Cli`: lightweight host over `Sharp.Core` (`run`/`repl`/`models`) for manual validation.
- `Sharp.Core.Tests`: unit and integration tests.

## Repository Layout

```text
SharpAgent/
├── Sharp.AI/              # Provider abstraction, streaming adapters
├── Sharp.Cli/             # Thin CLI host for end-to-end validation
├── Sharp.Cli.Tests/       # CLI tests
├── Sharp.Core/            # Agent runtime, session management, tools
├── Sharp.Core.Tests/      # Unit and integration tests
├── docs/
├── config.example.json
└── SharpAgent.sln
```

## Key Design Decisions

- **Library-first, host-second**: core logic stays in `Sharp.Core`; `Sharp.Cli` is intentionally thin.
- **Session model is JSONL tree-based** (`id` + `parentId`), enabling branch rebuild and deterministic context recovery.
- **Tool interface is structured**: `IAgentTool` uses JSON arguments and returns `ToolInvocationResult` with `isError`, `content`, and `details`.
- **Provider logic is isolated in `Sharp.AI`**; `Sharp.Core` does not depend on provider-specific wire formats.
- **Extension system**: `ExtensionRuntime` + `ExtensionLoader` support plugin discovery/loading with session before/after lifecycle hooks and explicit reload.
- **Environment-based credentials**: API keys can be injected via environment variables without storing them in config files.

## Build & Test

```bash
dotnet build SharpAgent.sln -m:1 -nr:false -v minimal
dotnet test SharpAgent.sln -m:1 -nr:false -v minimal

# Single test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
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

# note:
# - oauth login/logout commands are not implemented in Sharp.Cli yet
# - provide oauth credentials via config apiKey or *_ACCESS_TOKEN/*_OAUTH_TOKEN env vars
#
# runtime streams:
# - assistant text -> stdout
# - event trace (turn/thinking/tool lifecycle) -> stderr
# thinking events depend on provider/model and --thinking level
```

## Configuration Quick Start

- Default config path: `~/Library/Application Support/Sharp/config.json`
- Provider `api` uses pi-style values:
  - `openai-completions`
  - `openai-responses`
  - `anthropic-messages`
  - `google-gemini-cli`
- `api` is provider-level; model-level `api` is only kept for backward compatibility.
- Optional per-model metadata:
  - `capabilities` (`supportsReasoning`, `supportsImageInput`, `supportsToolCall`)
  - `pricing` (`inputPerMillionTokens`, `outputPerMillionTokens`, `cacheReadPerMillionTokens`, `cacheWritePerMillionTokens`)
- For `openai-completions`, optional per-model `compat` flags are supported, including:
  - `supportsStore`, `supportsDeveloperRole`, `supportsReasoningEffort`
  - `supportsUsageInStreaming`, `supportsStrictMode`
  - `requiresToolResultName`, `requiresAssistantAfterToolResult`, `requiresMistralToolIds`, `requiresThinkingAsText`
  - `maxTokensField`, `thinkingFormat` (`openai`/`zai`/`qwen`)
  - `openRouterRouting`, `vercelGatewayRouting`
- If `openai-completions` `compat` is omitted, SharpAgent infers known defaults from provider/baseUrl; explicit compat fields take precedence.
- API key/base URL can be injected by environment variables:
  - `SHARP_<PROVIDER_ID>_API_KEY`, `SHARP_<PROVIDER_ID>_ACCESS_TOKEN`, `SHARP_<PROVIDER_ID>_OAUTH_TOKEN`, `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_API_KEY`, `<PROVIDER_ID>_ACCESS_TOKEN`, `<PROVIDER_ID>_OAUTH_TOKEN`, `<PROVIDER_ID>_BASE_URL`
  - compatibility aliases for canonical provider ids only: `OPENAI_*`, `ANTHROPIC_*`, `KIMI_*`
  - antigravity alias: `ANTIGRAVITY_ACCESS_TOKEN`, `ANTIGRAVITY_BASE_URL`
  - provider-specific API key aliases: `HF_TOKEN`, `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`
  - credential headers are resolved per request, so token rotation via environment variables is picked up without process restart
  - `*_ACCESS_TOKEN` / `*_OAUTH_TOKEN` also accepts JSON envelope values (`token`/`access_token`/`access` + optional `expires_at`/`expires`/`expires_in`)
  - for `google-antigravity`, include `projectId` in the token envelope
    - minimal: `{"token":"...","projectId":"..."}`
    - refreshable: `{"access":"...","refresh":"...","expires":<oauth-expiry-unix-ms>,"projectId":"..."}`
- Current built-in `google-antigravity` models include:
  - `gemini-3-pro-high`
  - `gemini-3-pro-low`
  - `gemini-3-flash`
  - `claude-sonnet-4-5`
  - `claude-sonnet-4-5-thinking`
  - `claude-opus-4-5-thinking`
  - `claude-opus-4-6-thinking`
  - `gpt-oss-120b-medium`
- Pi-aligned built-in provider subset is generated from `https://models.dev/api.json` via
  `node scripts/generate-pi-builtin-providers.mjs`.
  In offline/sandboxed environments, use `--input-file scripts/fixtures/models.dev.pi-subset.sample.json`.
  This generator is intentionally scoped to the curated pi-aligned subset, not the full models.dev catalog.

## Key Abstractions

| Interface | Location | Purpose |
|-----------|----------|---------|
| `ILlmProvider` | `Sharp.AI` | Unified provider interface for streaming completions |
| `AgentSession` | `Sharp.Core` | High-level session control (prompt, continue, steer, abort) |
| `SessionManager` | `Sharp.Core` | JSONL tree persistence with branch rebuild |
| `IAgentTool` | `Sharp.Core` | Structured tool interface (JSON in, structured result out) |

### Session Model

Sessions are stored as JSONL with a tree structure:
- Line 1: `session` header with metadata
- Subsequent lines: entries with `id`, `parentId`, `type`, and `payload`
- `SessionManager.RebuildContext()` reconstructs context from the current leaf branch

### Tool Model

- `read`, `write`, `edit`, `bash` are the core built-in tools
- `ToolInvocationResult` returns structured data: `isError`, `content`, `details`
- `edit` requires unique text matching and returns diff metadata

## Current Scope

Implemented in this phase:

- Provider abstraction and streaming adapters for OpenAI Chat Completions, OpenAI Responses, and Anthropic Messages.
- Cross-provider handoff transforms for message replay (orphan tool-result backfill, tool-call ID normalization, unsigned thinking downgrade).
- Provider creation registry (`LlmProviderFactory.Register/Unregister`).
- Session-driven loop (`AgentSession` + `AgentLoop` + `ToolRuntime`).
- Session control surface (`ContinueAsync`, `Steer`, `FollowUp`, `Abort`, `WaitForIdleAsync`).
- Tree-structured JSONL session persistence (`SessionManager`).
- Session entries for compaction/branch summary/custom message/label.
- Built-in coding tools: `read`, `write`, `edit`, `bash`.
- Thin CLI host with `run`, `repl`, and `models`; REPL local commands include `:continue`, `:reload`, `:diag`, `:tree`, `:fork`, and `:switch`.
- CLI renders core lifecycle events for validation (turn/thinking/tool call/tool execution).
- Core + AI test coverage including an end-to-end session-loop-tool scenario.

Out of scope in this phase:

- TUI/Web UI/WinForms.
- Extension package/version management.
- Session compaction and branch summary UI.

## Known Constraints

- Some third-party "Anthropic-compatible" gateways may return empty streams or non-standard SSE events; the provider will surface a `no parseable events` error rather than silently completing.
- Extension loading uses `Assembly.LoadFrom` without `AssemblyLoadContext` isolation; plugins cannot be unloaded without process restart.
- Extension reload overwrites provider registrations; removed extensions do not automatically roll back their registrations.

## License

MIT
