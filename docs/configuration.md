# Configuration

Configuration is library-oriented in this phase.

## Config Model

`Sharp.Core.Configuration.AgentConfig`:

- `defaultModel`: `<provider>/<model>`.
- `providers[]`:
  - `id`
  - `api` (`openai-completions` or `anthropic-messages`)
  - `apiKey`
  - `baseUrl`
  - `models[]`:
    - `id`
    - `contextWindow`
    - `maxOutputTokens`

See `/Users/deepwind/repo/SharpAgent/config.example.json`.

## Runtime Builder

Use `AgentConfigurationService`:

1. `LoadFromFile(path)`
2. `BuildRuntimeOptions(...)`
3. `AgentSession.CreateAsync(options)`

Default filesystem locations:

- config: `~/Library/Application Support/Sharp/config.json` (via `DefaultConfigPath()`)
- sessions: `~/.sharp/sessions` (via `DefaultSessionDirectory()`)

CLI helpers:

- `sharp config init [--config <path>] [--force]`
- `sharp config validate [--config <path>]`
- `sharp config validate [--config <path>] --json`

## Environment Overrides

`AgentConfigurationService` supports:

- Provider-generic overrides (recommended):
  - `SHARP_<PROVIDER_ID>_API_KEY`
  - `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_API_KEY`
  - `<PROVIDER_ID>_BASE_URL`
- Compatibility aliases:
  - `OPENAI_API_KEY`
  - `OPENAI_BASE_URL`
  - `ANTHROPIC_API_KEY`
  - `ANTHROPIC_BASE_URL`
- Global model override:
  - `LLM_DEFAULT_MODEL`

`<PROVIDER_ID>` is normalized from `providers[].id` by upper-casing and replacing non-alphanumeric characters with `_`.

Examples:

- `openai` -> `SHARP_OPENAI_API_KEY`, `OPENAI_API_KEY`
- `kimi-coding` -> `SHARP_KIMI_CODING_API_KEY`, `KIMI_CODING_API_KEY`

## Notes

- There is no CLI/UI settings panel in this phase.
- Configuration APIs are intended for embedding into external host applications.
- Model-level `api` still loads for backward compatibility, but new configs should use provider-level `api`.
