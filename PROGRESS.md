# SharpAgent 进度报告（2026-02-10）

## 任务目标（确认）

核心目标保持不变：以 `pi-mono/packages/{ai,agent,coding-agent}` 为参考，实现 C# 版本，并按 C# 最佳实践做必要范式改造。  
当前阶段范围约束：

- TUI 暂不实现；CLI 先做薄宿主用于端到端验证。
- Skills 必须可用。
- 插件系统必须具备可扩展能力（先核心内核，再逐步补齐生态能力）。

## 总体完成度

1. `Sharp.AI`：基础可用，核心 provider 适配完成（OpenAI Chat Completions / Anthropic Messages）。
2. `Sharp.Core` 会话循环：核心链路可用（session + loop + tools + persistence）。
3. Skills：已接入并默认可加载（已标记完成）。
4. 插件系统：已完成“可用内核 + 发现装载 + session 生命周期 before/after 闭环 + reload 生命周期（core）”阶段，进入可迭代扩展阶段。
5. CLI：MVP 已落地（`run/repl/models`）；TUI：未实现。

## 增量同步（2026-02-10 晚）

### A. 工具回合签名链路修复（Anthropic）

- 问题：工具调用后第二轮请求触发 `Corrupted thought signature`。
- 已修复：
  - `tool_use` / `thinking` 的 `signature` 已在 `Sharp.AI` -> `Sharp.Core` -> session message 中完整透传。
  - `AnthropicLlmProvider` 现已解析并回放 signature，避免工具回合丢签名。
- 结果：
  - `gemini-3-flash` 工具调用回合恢复正常。

### B. CLI 文本可见性修复

- 问题：某些场景仅看到 `[result:end]`，看不到模型文本。
- 已修复：
  - `CliEventRenderer` 在没有 text delta 时，会在 completed 阶段回退输出 assistant 文本。
  - stdout 输出增加 flush，降低缓冲导致的“无输出”感知。

### C. Anthropic SSE 兼容与显式报错

- 已增强：
  - 支持 data-only SSE（仅 `data:`，无 `event:`）事件推断。
  - 当流中 0 个可解析事件时返回明确 `LlmErrorEvent(Validation)`，不再静默结束。
- 结论：
  - 对部分非标准 Anthropic 兼容网关（如当前 kimi-coding 配置路径）可快速暴露协议不兼容，而不是误判为“成功无输出”。

### D. 配置系统环境变量回退（已完成）

- 运行时已支持 provider 通用环境变量（优先于 config）：
  - `SHARP_<PROVIDER_ID>_API_KEY`
  - `<PROVIDER_ID>_API_KEY`
  - `SHARP_<PROVIDER_ID>_BASE_URL`
  - `<PROVIDER_ID>_BASE_URL`
- 兼容别名保留：
  - `OPENAI_*` / `ANTHROPIC_*`
- 文档已同步：
  - `docs/configuration.md`
  - `README.md`

## 已完成能力（详细）

### 1. AI 层（`Sharp.AI`）

- 统一消息与内容块模型。
- 统一 streaming 事件模型。
- OpenAI / Anthropic provider 适配。
- `LlmProviderFactory` 动态注册机制（支持扩展注入 provider factory）。

### 2. Agent 运行时（`Sharp.Core`）

- `AgentSession` + `AgentLoop` + `ToolRuntime` 主链路稳定。
- JSONL 树状会话存储（`SessionManager`，支持 `id/parentId` 分支恢复）。
- 内置工具集：`read` / `write` / `edit` / `bash` / `grep` / `find` / `ls`。
- 会话控制：`PromptAsync` / `ContinueAsync` / `Steer` / `FollowUp` / `Abort` / `WaitForIdleAsync`。

### 3. Skills 与资源加载（已完成）

- 支持 context files、append prompt、`SYSTEM.md` 发现、skills 聚合。
- Skills 诊断可聚合到资源诊断。
- read tool 在场时会将 skills 注入 system prompt。

### 4. 插件系统（本轮重点）

已完成插件契约与运行时内核：

- 扩展接口与事件契约：
  - input / context / before_agent_start
  - tool_call / tool_result
  - session_start / session_shutdown
  - resources_discover
- 扩展注册能力：
  - tool / command / flag / provider factory
- 运行时行为：
  - 输入 transform 链与 handled 短路
  - tool_call 阻断（含 handler 异常 fail-safe）
  - tool_result 链式 patch
  - 命令路由（`/command args`）
  - 资源发现结果聚合
  - 冲突检测（tool/command/flag 冲突时后注册扩展剔除）
  - `session_before_*` 预处理钩子（新增）：
    - `session_before_switch`（可取消）
    - `session_before_fork`（可取消）
    - `session_before_tree`（可取消 + 可改写目标与导航参数）
    - `session_before_compact`（可取消 + 可改写 compaction 入参）
  - `session_*` 后置事件（新增）：
    - `session_switch`
    - `session_fork`
    - `session_tree`
    - `session_compact`

已完成插件发现与装载（新增）：

- 新增 `ExtensionLoader`（`Sharp.Core/Extensions/ExtensionLoader.cs`）：
  - 默认目录发现：
    - `~/.sharp/extensions/`
    - `<cwd>/.sharp/extensions/`
  - 显式路径加载（`AgentRuntimeOptions.ExtensionPaths`）
  - 目录入口规则：
    - `extension.json`（`extensions` 列表）
    - 回退 `index.dll`
  - DLL 反射装载 `IAgentExtension`（公共、可实例化类型）
  - 装载诊断汇总为 `ExtensionDiagnostic`

插件接入运行时创建链路：

- `AgentRuntimeOptions` 新增：
  - `DiscoverExtensions`
  - `ExtensionPaths`
- `AgentSession.CreateAsync`：
  - 合并程序内注入扩展与发现到的扩展
  - 初始化 `ExtensionRuntime`
  - 传播扩展诊断到 `ResourceSnapshot.Diagnostics`

插件钩子接入 `AgentSession` 入口（新增）：

- `RequestSessionSwitchAsync(...)`：触发 `session_before_switch` 决策。
- `ForkBranchAsync(...)`：触发 `session_before_fork`（可取消）；成功后触发 `session_fork`。
- `NavigateTreeAsync(...)` / `SwitchBranchAsync(...)`：触发 `session_before_tree`（支持参数改写与取消）；成功后触发 `session_tree`。
- `AppendCompactionAsync(...)`：触发 `session_before_compact`（支持 compaction 参数改写与取消）；成功后触发 `session_compact`。
- `NotifySessionSwitchedAsync(...)`：触发 `session_switch`（与 `RequestSessionSwitchAsync(...)` 组成 before/after 对应入口）。

已完成插件 reload 生命周期（新增）：

- `AgentSession.ReloadExtensionsAsync(...)`：
  - 等待当前流式会话空闲后执行 reload。
  - 重新发现并加载扩展（保留 `options.Extensions` + 重新扫描目录）。
  - 重建 `ExtensionRuntime`、扩展工具适配器与 `ToolRuntime`。
  - 重新执行 `resources_discover(reason=Reload)` 并重建 system prompt / skills 快照。
  - 生命周期事件顺序：旧 runtime `session_shutdown` -> 新 runtime `session_start`。

### 5. CLI（新增：MVP）

- 新增 `Sharp.Cli` 项目并接入 `SharpAgent.sln`。
- 命令集：
  - `run <prompt...>`：单次会话执行并流式渲染文本。
  - `repl`：交互模式，复用同一 session。
  - `models`：读取配置并列出可用模型。
- 全局参数：
  - `--config` / `--model` / `--workdir` / `--session-dir` / `--agent-dir` / `--session`
  - `--thinking` / `--max-turns` / `--no-skills` / `--no-discover-extensions`
- REPL 本地命令：
  - `:continue` / `:reload` / `:diag` / `:session` / `:tree` / `:fork` / `:switch` / `:exit`
- 事件可观测性（新增）：
  - `run/repl` 在 `stderr` 输出基础事件 trace：`turn start/end`、`thinking start/end`、`tool call start/ready`、`tool execution start/update/end`、`result end`。
  - 工具调用参数会按 JSON 友好格式展示（`tool_call_id` 关联）。
  - 工具执行结果会展示结构化摘要（`isError`、`contentPreview`、`details`）。
- 会话头信息增强（新增）：
  - 输出当前 `model`、`thinking`、`max_turns`，便于 REPL 验证运行态配置。
- 设计原则：
  - 维持“core 在库内，CLI 只做编排与渲染”的薄宿主模式，避免把业务逻辑挪入入口层。

### 6. 配置系统（新增增强）

- 配置字段与 pi 风格进一步对齐：
  - `provider.api` 使用 kebab-case：`openai-completions` / `anthropic-messages`。
  - `model.api` 仅保留向后兼容读取，不再作为主配置入口。
- `AgentConfigurationService` 已实现结构化配置校验：
  - 校验 `defaultModel` 格式与可解析性；
  - 校验 provider/model 的存在性、唯一性、`baseUrl` 合法性、数值字段有效性；
  - 校验 default provider 的 API key 可用性；
  - 输出 warning（例如 legacy `model.api`）与 error 分离结果。
- `Sharp.Cli` 已新增配置命令：
  - `config init`（支持 `--force` 覆盖）
  - `config validate`
  - `config validate --json`（脚本友好输出）

## 测试与验证

新增并通过插件专项测试：

- `Sharp.Core.Tests/ExtensionRuntimeTests.cs`
- `Sharp.Core.Tests/ExtensionLoaderTests.cs`
- `Sharp.Cli.Tests/CliInvocationTests.cs`（CLI 参数解析）
- `Sharp.Cli.Tests/SharpCliAppConfigCommandTests.cs`（config init/validate 行为）
- `Sharp.Cli.Tests/CliEventRendererTests.cs`（REPL/Run 事件渲染：thinking/tool lifecycle）
- `Sharp.Core.Tests/AgentConfigurationServiceTests.cs`（配置校验与兼容性）

本地验证命令：

```bash
dotnet build SharpAgent.sln -m:1 -nr:false -v minimal
dotnet test SharpAgent.sln -m:1 -nr:false -v minimal
```

最新结果：

- Build: 成功（0 errors）
- Test: 145 total / 140 passed / 5 skipped / 0 failed

## 与 pi-mono 的差距（当前仍存在）

1. 插件能力差距（主要在 coding-agent 的 UI/交互层）

- 尚未实现 extension UI context（select/input/custom component 等）。
- 尚未实现 shortcut 系统与冲突策略。
- 已实现 core reload 生命周期，但尚未实现文件监听驱动的自动热重载策略。

2. 生态与分发

- 尚未实现 package 级资源管理（npm/git 对应能力）。
- 当前是 DLL 发现装载，不含版本管理与隔离策略。

3. CLI / TUI

- CLI 仅完成 MVP；尚未覆盖完整的 `session` 子命令族与脚本化输出模式（如 JSONL）。
- TUI 仍未实现。

## 当前已知限制 / 风险

1. `resources_discover` 中 `promptPaths/themePaths` 仍未消费，当前仅给 warning 诊断。
2. 插件 DLL 当前使用 `Assembly.LoadFrom`，未做独立 `AssemblyLoadContext` 隔离与卸载。
3. reload 后 provider factory 仅支持“再次注册覆盖”，当前未实现移除旧扩展时的 factory 回滚策略。
4. `session_switch` 目前是“两阶段调用”模型：`RequestSessionSwitchAsync`（before）与 `NotifySessionSwitchedAsync`（after）由上层编排触发，不是 core 内部自动切换 session 文件。
5. 与 pi 的 TypeScript 扩展生态不兼容（这是语言迁移后的预期差异，需要 C# 生态等价替代）。
6. CLI 已具备人类可读事件 trace，但尚未提供结构化事件输出（JSON/JSONL）与非交互脚本友好退出码细分。
7. 当前 `kimi-coding/claude-sonnet-4-20250514` 在 `anthropic-messages` 协议路径下仍可能返回“无可解析事件”流，需 provider 专项适配。

## 下一步建议（按优先级）

1. 引入可控的插件隔离装载（`AssemblyLoadContext` + 依赖解析策略 + 卸载策略）。
2. 在现有 `ReloadExtensionsAsync` 之上补自动热重载触发（watcher + debounce + 并发保护）。
3. 设计 C# 插件包规范（manifest + 多资源：extensions/skills/prompts/themes）。
4. 为 CLI 增加结构化输出模式（JSON/JSONL）与更完整的会话管理子命令（tree/switch/fork/compact/continue）。
5. 固化 `Sharp.Core.Extensions` 公共 API 与兼容策略（含 `session_switch` 两阶段编排约定与 reload 语义）。

## 执行计划（2026-02-10 -> 2026-02-12）

### P0: 插件隔离装载（ALC）

目标：让扩展具备“可卸载、可重载、依赖隔离”的运行时边界，为热重载打基础。

- 引入 `PluginLoadContext : AssemblyLoadContext`（collectible）。
- `ExtensionLoader` 从 `Assembly.LoadFrom` 迁移为 ALC 装载路径。
- 为每个扩展包建立独立 load context，并记录可回收句柄。
- 补充测试：
  - 同名依赖不同版本的并存加载；
  - reload 后旧 context 可释放（弱引用可回收）。

### P1: 自动热重载触发

目标：在不干扰运行中会话的前提下，将手动 reload 升级为“目录变更触发 + 合并抖动 + 安全串行”。

- 在宿主层增加 watcher（先覆盖 `.sharp/extensions` 与配置路径）。
- 设计 debounce 策略（建议 300-1000ms 窗口）。
- watcher 仅发出 reload 请求，真正执行仍走 `AgentSession.ReloadExtensionsAsync` 串行锁。
- 补充测试：
  - 短时间多次文件变更只触发一次 reload；
  - 流式运行中变更会在空闲后生效，不中断当前回合。

### P2: Provider Factory 回滚策略

目标：解决“扩展移除后 provider factory 残留覆盖”的一致性问题。

- 在 core 内维护 provider factory owner 映射（apiKind -> extension identity）。
- reload 时先 diff old/new 扩展集合，再执行 unregister/register。
- 失败回滚策略：注册失败时恢复到上一个稳定映射。
- 补充测试：
  - 移除扩展后 factory 恢复到默认或前一覆盖者；
  - 多扩展覆盖顺序稳定且可预测。
