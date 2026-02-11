# Configuration

Configuration is library-oriented in this phase.

## Config Model

`Sharp.Core.Configuration.AgentConfig`:

- `defaultModel`: `<provider>/<model>`.
- `providers[]`:
  - `id`
  - `api` (`openai-completions`, `openai-responses`, `anthropic-messages`, or `google-gemini-cli`)
  - `apiKey`
  - `baseUrl`
  - `models[]`:
    - `id`
    - `contextWindow`
    - `maxOutputTokens`
    - `capabilities` (optional):
      - `supportsReasoning` (default inferred by `api`)
      - `supportsImageInput` (default inferred by `api`)
      - `supportsToolCall` (default inferred by `api`)
    - `pricing` (optional, prices per 1M tokens):
      - `inputPerMillionTokens`
      - `outputPerMillionTokens`
      - `cacheReadPerMillionTokens`
      - `cacheWritePerMillionTokens`
    - `compat` (optional, `openai-completions` only):
      - `supportsStore` (default inferred from provider/baseUrl)
      - `supportsDeveloperRole` (default inferred from provider/baseUrl)
      - `supportsReasoningEffort` (default inferred from provider/baseUrl)
      - `supportsUsageInStreaming` (default `true`)
      - `supportsStrictMode` (default `true`)
      - `requiresToolResultName` (default `false`)
      - `requiresAssistantAfterToolResult` (default `false`)
      - `requiresMistralToolIds` (default `false`)
      - `requiresThinkingAsText` (default `false`)
      - `maxTokensField` (`max_tokens` or `max_completion_tokens`, default inferred from provider/baseUrl)
      - `thinkingFormat` (`openai`, `zai`, or `qwen`, default inferred from provider/baseUrl)
      - `openRouterRouting` (optional routing object: `only[]`, `order[]`)
      - `vercelGatewayRouting` (optional routing object: `only[]`, `order[]`)

When `compat` is not set, `openai-completions` now applies URL/provider-based defaults for known endpoints (for example, Mistral-specific tool-id/name/thinking behavior).  
When `compat` is partially set, explicit fields override inferred defaults.
`openRouterRouting` is only applied when `baseUrl` points to OpenRouter; `vercelGatewayRouting` is only applied when `baseUrl` points to Vercel AI Gateway.

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

OAuth CLI status:

- `sharp login` / `sharp logout` are not implemented yet.
- For now, provide OAuth credentials through `providers[].apiKey` or `*_ACCESS_TOKEN` / `*_OAUTH_TOKEN`.

## Environment Overrides

`AgentConfigurationService` supports:

- Provider-generic overrides (recommended):
  - `SHARP_<PROVIDER_ID>_API_KEY`
  - `SHARP_<PROVIDER_ID>_ACCESS_TOKEN`
  - `SHARP_<PROVIDER_ID>_OAUTH_TOKEN`
  - `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_API_KEY`
  - `<PROVIDER_ID>_ACCESS_TOKEN`
  - `<PROVIDER_ID>_OAUTH_TOKEN`
  - `<PROVIDER_ID>_BASE_URL`
- Compatibility aliases (only for canonical provider ids):
  - `OPENAI_API_KEY`
  - `OPENAI_BASE_URL`
  - `ANTHROPIC_API_KEY`
  - `ANTHROPIC_BASE_URL`
  - `KIMI_API_KEY`
  - `KIMI_BASE_URL`
  - `ANTIGRAVITY_ACCESS_TOKEN`
  - `ANTIGRAVITY_BASE_URL`
- Provider-specific API key aliases:
  - `HF_TOKEN` (`huggingface`)
  - `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN` (`github-copilot`)
- Global model override:
  - `LLM_DEFAULT_MODEL`

Credential headers are resolved per request, so rotating `<PROVIDER_ID>_ACCESS_TOKEN`/`<PROVIDER_ID>_OAUTH_TOKEN` in the environment can take effect without restarting the process.

`<PROVIDER_ID>_ACCESS_TOKEN` / `<PROVIDER_ID>_OAUTH_TOKEN` can be either:

- plain token string
- JSON envelope with fields:
  - token field: `token` or `access_token` or `value` or `access`
  - optional refresh token: `refresh` or `refresh_token`
  - optional expiry: `expires` or `expires_at` (ISO8601 or unix epoch) or `expires_in` (seconds)

For `google-antigravity`, include `projectId` in the envelope, for example:
`{"token":"ya29...","projectId":"my-google-project"}`.

For auto-refresh capable `google-antigravity` credentials, use:
`{"access":"ya29...","refresh":"1//...","expires":<oauth-expiry-unix-ms>,"projectId":"my-google-project"}`.

Built-in `google-antigravity` models:

- `gemini-3-pro-high`
- `gemini-3-pro-low`
- `gemini-3-flash`
- `claude-sonnet-4-5`
- `claude-sonnet-4-5-thinking`
- `claude-opus-4-5-thinking`
- `claude-opus-4-6-thinking`
- `gpt-oss-120b-medium`

`<PROVIDER_ID>` is normalized from `providers[].id` by upper-casing and replacing non-alphanumeric characters with `_`.

Examples:

- `openai` -> `SHARP_OPENAI_API_KEY`, `SHARP_OPENAI_ACCESS_TOKEN`, `OPENAI_API_KEY`, `OPENAI_ACCESS_TOKEN`
- `anthropic` -> `SHARP_ANTHROPIC_API_KEY`, `SHARP_ANTHROPIC_ACCESS_TOKEN`, `ANTHROPIC_API_KEY`, `ANTHROPIC_ACCESS_TOKEN`
- `kimi-coding` -> `SHARP_KIMI_CODING_API_KEY`, `SHARP_KIMI_CODING_ACCESS_TOKEN`, `KIMI_CODING_API_KEY`, `KIMI_CODING_ACCESS_TOKEN`, `KIMI_API_KEY`, `KIMI_ACCESS_TOKEN`
- `google-antigravity` -> `SHARP_GOOGLE_ANTIGRAVITY_ACCESS_TOKEN`, `GOOGLE_ANTIGRAVITY_ACCESS_TOKEN`, `ANTIGRAVITY_ACCESS_TOKEN`
- `huggingface` -> `SHARP_HUGGINGFACE_API_KEY`, `HUGGINGFACE_API_KEY`, `HF_TOKEN`
- `github-copilot` -> `SHARP_GITHUB_COPILOT_API_KEY`, `GITHUB_COPILOT_API_KEY`, `GH_TOKEN`

## Notes

- There is no CLI/UI settings panel in this phase.
- OAuth auth storage (`auth.json`) and interactive login flow are planned but not shipped yet.
- OAuth test hook (for integration tests only): `SHARP_ANTIGRAVITY_OAUTH_TOKEN_ENDPOINT`
  (compat alias: `ANTIGRAVITY_OAUTH_TOKEN_ENDPOINT`).
- Configuration APIs are intended for embedding into external host applications.
- Model-level `api` still loads for backward compatibility, but new configs should use provider-level `api`.
- Pi-aligned built-in provider subset in `AgentConfig` is generated from
  `https://models.dev/api.json` by
  `node scripts/generate-pi-builtin-providers.mjs`.
  For offline updates, use `--input-file scripts/fixtures/models.dev.pi-subset.sample.json`.
  The generator is intentionally limited to the curated pi-aligned built-in subset, not all models.dev providers.
