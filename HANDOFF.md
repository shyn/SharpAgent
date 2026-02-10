# Handoff（2026-02-10）

## 会话目标与当前结论

本轮目标是继续推进 `Sharp.AI` 对标 `pi-mono/packages/ai` 的 P0 阶段，重点落地：

1. `stopReason` 语义闭环；
2. `openai-responses` provider 接入；
3. 跨请求消息预处理（至少补齐 orphan tool result）；
4. `openai-completions` 的第二批兼容开关（compat）起步实现。

当前结论：上述 1~4 已完成首批可用实现，且已通过全量测试。可以继续在此基础上扩展 compat 细项与 provider 差异检测策略。

### 本次增量（continue）

已补齐 `supportsStrictMode`：

- `OpenAiCompletionsCompat` 新增 `SupportsStrictMode`（默认 `true`）。
- `models[].compat.supportsStrictMode` 已打通配置解析。
- `OpenAiLlmProvider` 工具定义行为对齐 pi：
- `supportsStrictMode=true` 时发送 `"strict": false`；
- `supportsStrictMode=false` 时省略 `strict` 字段。
- 已补充配置解析与 payload 断言测试。

已补齐 URL-based compat 默认检测（`openai-completions`）：

- 新增 `OpenAiCompatResolver`，规则顺序为：
  - `explicit model.compat`（逐字段）
  - `provider/baseUrl inferred defaults`
  - global defaults
- 已接入 `AgentConfigurationService.BuildRuntimeOptions`。
- 首批内置推断规则：
  - Mistral（`providerId=mistral` 或 `baseUrl` 含 `mistral.ai`）：
    - `requiresToolResultName=true`
    - `requiresMistralToolIds=true`
    - `requiresThinkingAsText=true`
    - `maxTokensField=max_tokens`
  - Chutes（`baseUrl` 含 `chutes.ai`）：
    - `maxTokensField=max_tokens`
  - gatewayz（`baseUrl` 含 `gatewayz.ai`）：
    - `supportsUsageInStreaming=false`
  - 其他：
    - `maxTokensField=max_completion_tokens`
- 已补充两条配置侧测试：
  - Mistral URL 自动推断；
  - 显式 compat 对推断值逐字段覆盖。

本次并行增量（subagents）：

- URL 推断矩阵与优先级补齐：
  - 修正 `xai` URL 识别：`baseUrl` 命中 `api.x.ai` 时，按 non-standard 处理（`supportsStore=false`、`supportsDeveloperRole=false`、`supportsReasoningEffort=false`）。
  - 新增配置侧矩阵测试覆盖：`chutes.ai`、`gatewayz.ai`、`deepseek.com`、`zai`、`qwen(dashscope)`、`xai(api.x.ai)`。
  - 新增显式覆盖优先级测试：`gatewayz` 推断值可被 `models[].compat.*` 逐字段覆盖。
- payload 侧断言补齐：
  - 扩展 `store`、`developer role`、`reasoning_effort` 的 provider/baseUrl 双路径断言（含 `xai` provider 与 `api.x.ai` URL）。
  - 新增 `thinkingFormat` 的 baseUrl 自动识别断言（`z.ai` -> `thinking.type`，`dashscope` -> `enable_thinking`）。
  - routing gate 断言改为明确由 baseUrl 决定（OpenRouter/Vercel）。
- cross-provider transform（本会话新增）：
  - `MessageTransforms` 新增 `NormalizeToolCallIds` 与 `ConvertUnsignedThinkingToText`。
  - `AnthropicLlmProvider` 接入：`EnsureToolResultContinuity` + toolCallId 归一化 + 无签名 thinking 转 text。
  - `OpenAiLlmProvider` 接入 OpenAI toolCallId 归一化（覆盖 Responses -> Completions 的 `|`/非法字符/超长 id）。
  - 新增跨 provider 回归：`OpenAI Responses -> OpenAI Completions`、`OpenAI Responses -> Anthropic`。
- 已完成并行回归：
  - 定向：`LlmProviderMappingTests + AgentConfigurationServiceTests` 全部通过。
  - 全量：`SharpAgent.sln` 通过。

---

## 工作区状态（未提交）

当前工作区包含以下改动（均为本轮有效变更）：

- Modified
  - `README.md`
  - `Sharp.AI/LlmProviderFactory.cs`
  - `Sharp.AI/LlmStreamEvent.cs`
  - `Sharp.AI/ModelDescriptor.cs`
  - `Sharp.AI/ProviderApiKind.cs`
  - `Sharp.AI/Providers/AnthropicLlmProvider.cs`
  - `Sharp.AI/Providers/OpenAiLlmProvider.cs`
  - `Sharp.AI/ToolCallIdNormalizer.cs`
  - `Sharp.Core.Tests/AgentConfigurationServiceTests.cs`
  - `Sharp.Core.Tests/LlmProviderMappingTests.cs`
  - `Sharp.Core/Configuration/AgentConfig.cs`
  - `Sharp.Core/Configuration/AgentConfigurationService.cs`
  - `config.example.json`
  - `docs/configuration.md`
- Added
  - `Sharp.AI/MessageTransforms.cs`
  - `Sharp.AI/OpenAiCompat.cs`
  - `Sharp.AI/Providers/OpenAiResponsesLlmProvider.cs`
  - `Sharp.Core/Configuration/OpenAiCompatResolver.cs`

---

## 本轮已完成实现（关键语义）

### 1) `stopReason` 语义闭环

- 在 `Sharp.AI` 增加统一结束原因枚举：
  - `LlmStopReason = Stop / Length / ToolUse / Aborted / Error`
- `LlmCompletedEvent` 新增 `StopReason` 字段（带默认值，兼容旧调用点）。
- OpenAI Chat Completions:
  - `finish_reason` 映射到 `LlmStopReason`。
- Anthropic Messages:
  - 从 `message_delta.delta.stop_reason` 映射到 `LlmStopReason`。

涉及文件：

- `Sharp.AI/LlmStreamEvent.cs`
- `Sharp.AI/Providers/OpenAiLlmProvider.cs`
- `Sharp.AI/Providers/AnthropicLlmProvider.cs`

### 2) `openai-responses` provider 新增

新增 `OpenAiResponsesLlmProvider`，支持：

- 请求：POST `/responses`
- SSE 事件解析（核心事件）：
  - `response.output_item.added`
  - `response.output_text.delta`
  - `response.reasoning_summary_text.delta`
  - `response.function_call_arguments.delta`
  - `response.output_item.done`
  - `response.completed`
  - `response.failed` / `error`
- 聚合 thinking/text/toolcall/usage，并输出 `LlmCompletedEvent`
- 错误映射、Retry-After 上限策略、debug/payload hook

涉及文件：

- `Sharp.AI/Providers/OpenAiResponsesLlmProvider.cs`
- `Sharp.AI/ProviderApiKind.cs`
- `Sharp.AI/LlmProviderFactory.cs`

### 3) 消息预处理抽象（跨 provider 复用）

新增 `MessageTransforms`：

- `EnsureToolResultContinuity`: 补齐 orphan tool call 的 synthetic tool result（`No result provided`）。
- `EnsureAssistantAfterToolResult`: 在需要的方言下，tool result 后遇到 user 时插入空 assistant。
- `NormalizeToolCallIds`: 统一重写 assistant/tool 的 toolCallId 映射（用于跨 provider handoff）。
- `ConvertUnsignedThinkingToText`: 将无签名 thinking 降级为 text（保留有签名 thinking 供同源 replay）。

目前接入：

- `OpenAiLlmProvider`
- `OpenAiResponsesLlmProvider`
- `AnthropicLlmProvider`

涉及文件：

- `Sharp.AI/MessageTransforms.cs`
- `Sharp.AI/Providers/OpenAiLlmProvider.cs`
- `Sharp.AI/Providers/OpenAiResponsesLlmProvider.cs`

### 4) `openai-completions` compat（第二批首版）

新增 compat 模型并打通配置 -> runtime -> provider：

- 新类型：
  - `OpenAiCompletionsCompat`
  - `OpenAiMaxTokensField`
- `ModelDescriptor` 新增 `OpenAiCompletionsCompat` 字段。
- `AgentConfig` 新增 `models[].compat` 配置结构及转换逻辑。

已支持开关：

- `supportsUsageInStreaming`
- `supportsStrictMode`
- `requiresToolResultName`
- `requiresAssistantAfterToolResult`
- `requiresMistralToolIds`
- `requiresThinkingAsText`
- `maxTokensField` (`max_tokens` / `max_completion_tokens`)

其中 `requiresMistralToolIds` 已实现 9 位字母数字 ID 归一化。

涉及文件：

- `Sharp.AI/OpenAiCompat.cs`
- `Sharp.AI/ModelDescriptor.cs`
- `Sharp.AI/ToolCallIdNormalizer.cs`
- `Sharp.AI/Providers/OpenAiLlmProvider.cs`
- `Sharp.Core/Configuration/AgentConfig.cs`
- `Sharp.Core/Configuration/AgentConfigurationService.cs`
- `config.example.json`
- `docs/configuration.md`
- `README.md`

---

## 已新增/更新测试

### Provider 映射与兼容测试

`Sharp.Core.Tests/LlmProviderMappingTests.cs` 新增或增强：

- OpenAI `finish_reason=length` -> `LlmStopReason.Length`
- Anthropic `message_delta.stop_reason=tool_use` -> `LlmStopReason.ToolUse`
- OpenAI orphan tool call 自动补 tool result
- OpenAI Responses 流组装（thinking/toolcall/usage/stopReason）
- OpenAI Responses orphan tool call 自动补 `function_call_output`
- OpenAI compat flags 对 payload 的影响验证
- OpenAI Mistral tool id 归一化验证

### 配置解析测试

`Sharp.Core.Tests/AgentConfigurationServiceTests.cs` 新增：

- `openai-responses` API 配置解析
- `models[].compat` 解析到 `ModelDescriptor.OpenAiCompletionsCompat`

---

## 验证结果

执行命令：

```bash
dotnet test SharpAgent.sln -m:1 -nr:false -v minimal
```

结果：

- `Sharp.Core.Tests`: 177 total / 172 passed / 5 skipped / 0 failed
- `Sharp.Cli.Tests`: 17 total / 17 passed / 0 failed
- 仅有已知环境告警：`NU1900`（nuget vulnerability source 不可达）

---

## 已知限制与待继续项

### A. compat 第二批（配置侧）已补齐

本轮已在配置模型/解析链路补齐：

1. `supportsStore`
2. `supportsDeveloperRole`
3. `supportsReasoningEffort`
4. `thinkingFormat`（openai/zai/qwen）
5. `openRouterRouting` / `vercelGatewayRouting`

语义与优先级：

- 保持既有默认推断策略（provider/baseUrl inferred defaults）。
- 显式 `models[].compat.*` 逐字段覆盖推断值。
- 非法 `thinkingFormat` 会抛出配置解析异常（`JsonException`）。

### B. OpenAI Responses 适配仍有可增强点

1. 事件覆盖仍是核心集，不是全量 Responses 事件；
2. cross-provider handoff 已补首批关键路径（id 归一化 + unsigned thinking 降级 + Responses->Completions/Anthropic 回归），但与 pi 的全量 `transformMessages` 仍有差距（如 same-model vs cross-model 的精细分流）；
3. usage 成本仍未接入真实费率计算（当前 cost=0）。

### C. baseUrl 自动兼容检测已扩展

当前已覆盖：`mistral/chutes/gatewayz/deepseek/zai/qwen/xai(api.x.ai)`，且已用配置侧测试锁定“显式覆盖 > 推断默认”。后续如新增 provider，可继续按同模式增补。

---

## 下一步工作建议（可直接开工）

### P0-Next-1：扩 OpenAI Responses 事件覆盖

目标：

- 在 `OpenAiResponsesLlmProvider` 补齐更多官方/兼容事件类型（保持“可解析失败即显式报错”策略）。
- 为新增事件补 payload->stream event 的映射测试，避免 silent drop。

### P0-Next-2：细化 cross-provider message transform 对齐

最小增量测试：

1. 对齐 `pi-mono` 的 `transformMessages` 剩余细节（same-model 与 cross-model 的差异策略）
2. 补异常路径断言（非法消息形态、空 payload、重复 done 事件）
3. 增加更多跨 provider 混排回归（tool+thinking+aborted/error 历史）

---

## 建议的执行顺序（下一会话）

```text
1) 扩 OpenAI Responses 事件覆盖与测试
2) 补 cross-provider transform 剩余细节与异常路径
3) 回归全量测试并更新 handoff
```

---

## 备注

- 当前变更均未提交。
- 这份 handoff 已覆盖旧内容，可直接作为下一步执行上下文使用。
