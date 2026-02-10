# Handoff（2026-02-10）

## 会话目标

围绕 `pi-mono/packages/{ai,agent,coding-agent}` 对齐 C# 版本实现，阶段重点是：

1. 用 `Sharp.Cli` 端到端验证 `Sharp.Core` 行为；
2. 修复工具回合与流式可观测性的关键问题；
3. 完成配置系统的环境变量回退能力；
4. 保持库优先架构，避免把业务逻辑迁入 CLI。

## 本轮关键完成项

### 1. Anthropic 工具回合签名链路修复（P0）

问题：工具调用后第二轮请求报 `Corrupted thought signature`。

已完成：

- 在 AI 内容模型中补齐 signature 透传字段：
  - `ToolCall.Signature`
  - `ToolCallContentBlock.Signature`
  - `ThinkingContentBlock.Signature`
  - `LlmCompletedEvent.ThinkingSignature`
- `AnthropicLlmProvider` 已支持：
  - 解析 `tool_use.signature`
  - 解析 `thinking.signature_delta`
  - 回放 assistant 历史消息时带回 `tool_use/thinking signature`
- `AgentLoop` 持久化 assistant message 时保留上述签名信息。

结果：

- `gemini-3-flash` 场景下，工具调用 + 回传链路恢复正常。

### 2. CLI 输出可见性修复（P0）

问题：部分场景仅出现 `[result:end]`，没有模型文本。

已完成：

- `CliEventRenderer` 增加 completion 回退输出：
  - 若本轮无 `AgentTextDeltaEvent`，在 `AgentCompletedEvent` 时从 assistant message 中提取文本并输出。
- 为 stdout 输出和收尾换行增加 `Flush()`，降低缓冲导致的“看不到输出”感知。
- 增加测试覆盖：
  - 无 delta 时 fallback 可见；
  - 有 delta 时不会重复输出。

### 3. Anthropic SSE 兼容与错误显式化（P0）

问题：`kimi-coding/claude-sonnet-4-20250514` 返回 `200` 但无可解析事件，之前会静默结束。

已完成：

- `AnthropicLlmProvider.ReadSseAsync` 增加 data-only SSE 兼容：
  - 支持仅有 `data:` 行、无 `event:` 行的事件推断（从 JSON `type` 字段推断）。
- 增加“空可解析事件”显式错误：
  - 当流中 `parseableEventCount == 0` 时返回 `LlmErrorEvent(Validation)`，提示 provider 可能不兼容 Anthropic Messages SSE 格式。

结果：

- 对不兼容网关不再静默 `[result:end]`，而是明确报错，便于快速定位。

### 4. 配置系统环境变量回退能力完成（P0）

目标：不在 `config.json` 存明文 key，运行时可自动读取环境变量。

已完成：

- `AgentConfigurationService` 中 `ValidateConfig` 与 `BuildRuntimeOptions` 统一改为“解析后的 API key/baseUrl”逻辑。
- Provider 级环境变量候选规则：
  - `SHARP_<PROVIDER_ID>_API_KEY`
  - `<PROVIDER_ID>_API_KEY`
  - `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_BASE_URL`
- 兼容别名保留：
  - `OPENAI_API_KEY` / `OPENAI_BASE_URL`
  - `ANTHROPIC_API_KEY` / `ANTHROPIC_BASE_URL`
- `providerId` 规范化规则：
  - upper-case + 非字母数字替换为 `_`
  - 例如 `kimi-coding` -> `KIMI_CODING`
- CLI `config init` 提示文案已同步更新。

结果：

- 可以将 `providers[].apiKey` 置空，通过环境变量运行。

## 文档同步

已同步：

- `AGENTS.md`：更新当前阶段状态（CLI 已上线、插件与配置新能力、已知约束）。
- `README.md`：补充环境变量注入说明。
- `docs/configuration.md`：补充 provider 通用 env var 规则与示例。

## 验证结果

执行命令：

```bash
dotnet test SharpAgent.sln -m:1 -nr:false -v minimal
```

结果：

- 通过：`145 total / 140 passed / 5 skipped / 0 failed`
- 环境警告：`NU1900`（nuget vulnerability source 不可达）仍存在，不阻断。

## 当前风险 / 待办

1. `kimi-coding` 当前在 `anthropic-messages` 路径下仍可能返回“无可解析事件”流；
   - 已由静默失败改为显式错误；
   - 仍需做 provider 专用 adapter 或协议层降级。
2. 插件装载仍是 `Assembly.LoadFrom`；未实现 `AssemblyLoadContext` 隔离与可卸载。
3. reload 后 provider factory 仍为覆盖注册模型，未做移除回滚。
4. CLI 目前以人类可读输出为主，尚缺 JSON/JSONL 结构化事件输出。

## 下一会话建议优先级

1. P0：设计并实现 `kimi-coding` provider 适配策略（或独立 `api` kind），避免走“伪 Anthropic”协议。
2. P1：插件 `AssemblyLoadContext` 隔离加载与卸载验证。
3. P2：CLI 增加结构化输出模式（JSON/JSONL）用于自动化回归。
4. P3：provider factory owner 映射与 reload 回滚策略。
