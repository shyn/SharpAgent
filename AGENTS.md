# SharpAgent 项目说明（库优先阶段）

## 目录结构

```text
.
├── AGENTS.md
├── HANDOFF.md
├── README.md
├── config.example.json
├── docs/
├── Sharp.AI/
├── Sharp.Cli/
├── Sharp.Cli.Tests/
├── Sharp.Core/
├── Sharp.Core.Tests/
├── SharpAgent.sln
└── pi-mono -> ../pi-mono
```

## 项目定位（重要）

1. `SharpAgent` 是对 `pi-mono`（重点参考 `packages/ai`、`packages/agent`、`packages/coding-agent`）的 **C# rewrite**。
2. 当前仓库根目录已提供软链接：`./pi-mono -> ../pi-mono`。
3. 在实现、修复和重构时，若涉及行为语义、事件模型、会话格式、工具协议与边界条件，默认应优先对照 `pi-mono`。

## 参考策略（pi-mono）

1. 优先读文档：先明确目标语义和边界，再动代码。
2. 再对照实现：以 `pi-mono` 对应模块代码作为行为基线。
3. 最后对照测试：遇到不确定语义时，以 `pi-mono` 测试用例校准预期。
4. 若 `SharpAgent` 因平台或架构差异无法完全一致：
   - 在 PR/提交说明中明确差异与理由；
   - 在 `Sharp.Core.Tests` / `Sharp.Cli.Tests` 补充对应测试，固定当前行为。

## C# 重写原则（重要）

1. 目标是 **语义对齐**，不是逐行/逐语法翻译；在保证行为一致的前提下，应优先采用 C#/.NET 的惯用写法。
2. 重写过程需尽可能使用 C# 编程风格和范式：清晰类型边界、面向接口设计、异步 `async/await` + `CancellationToken`、必要时使用 `IAsyncEnumerable`。
3. 避免机械照搬 TypeScript/JavaScript 形态（例如过度动态结构、弱类型约定）；应改写为符合 .NET 生态的可维护实现。
4. 当“原实现写法”与 “C# 最佳实践”冲突时，优先选择：正确性与可维护性 > 性能 > 形式一致性，并用测试锁定行为等价。
5. 命名、异常模型、序列化边界、配置与扩展点设计应符合 C# 社区最佳实践，确保长期演进成本可控。

## 当前阶段状态

1. 已完成从多入口应用到库优先架构的破坏性重构。
2. 已移除 `SharpAgent.Console`、`SharpAgent.Api`、`SharpAgent.WinForms`。
3. 解决方案当前由 `Sharp.AI`、`Sharp.Core`、`Sharp.Cli`、对应测试项目组成。
4. `Sharp.Cli` 已作为薄宿主用于端到端验证（`run/repl/models/config`）；`TUI` 仍未实现。
5. 插件系统核心能力已具备：发现/装载、session before/after 生命周期、显式 reload 生命周期。
6. 配置系统已支持 provider 级环境变量回退（无需在 config 写明文 API key）。

## 核心职责分层

1. `Sharp.AI`
   - 统一消息与内容块模型。
   - 统一 provider streaming 事件模型。
   - OpenAI / Anthropic 适配器。
   - 关键兼容增强：
     - tool/thinking signature 透传（Anthropic 工具回合）。
     - data-only SSE 兼容解析。
2. `Sharp.Core`
   - 会话驱动循环：`AgentSession`、`AgentLoop`、`ToolRuntime`。
   - JSONL 树会话存储：`SessionManager`（`id` + `parentId`）。
   - 工具接口：`IAgentTool`（结构化参数与结构化结果）。
   - 扩展运行时：`ExtensionRuntime` + `ExtensionLoader` + 生命周期事件。
3. `Sharp.Cli`
   - Thin host：交互输入、事件渲染、配置与模型管理命令。
   - 约束：业务逻辑尽量留在 `Sharp.Core`，CLI 仅编排与展示。
4. `Sharp.Core.Tests`
   - 单测 + 集成测试。
   - 覆盖 loop、session、tool、provider mapping。

## 主要接口

- `ILlmProvider`（`/Users/deepwind/repo/SharpAgent/Sharp.AI/ILlmProvider.cs`）
- `AgentSession`（`/Users/deepwind/repo/SharpAgent/Sharp.Core/AgentSession.cs`）
- `SessionManager`（`/Users/deepwind/repo/SharpAgent/Sharp.Core/Sessions/SessionManager.cs`）
- `IAgentTool`（`/Users/deepwind/repo/SharpAgent/Sharp.Core/IAgentTool.cs`）

## 会话模型

1. 会话文件为 JSONL。
2. 第一行是 `session` header。
3. 后续 entry 带 `id`、`parentId`、`type`、`payload`。
4. `SessionManager.RebuildContext()` 按当前叶子分支恢复上下文。

## 工具模型

1. 工具集合收敛为 `read`、`write`、`edit`、`bash`。
2. `ToolInvocationResult` 统一包含 `isError`、`content`、`details`。
3. `edit` 需要唯一匹配，返回变更细节（包括 diff 元数据）。

## 构建与测试

- Build: `dotnet build SharpAgent.sln -m:1 -nr:false -v minimal`
- Test: `dotnet test SharpAgent.sln -m:1 -nr:false -v minimal`
- Single test: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`

## 已知约束

1. 当前环境可能出现 `NU1900` 警告（NuGet vulnerability source 不可达），不阻断 build/test。
2. 部分第三方“Anthropic 兼容”网关并不严格遵循 Anthropic Messages SSE 协议，可能返回空流/非标准事件。
   - 当前行为：会明确返回 `no parseable events` 错误，而不是静默 `[result:end]`。
3. 插件装载仍基于 `Assembly.LoadFrom`，尚未上 `AssemblyLoadContext` 隔离与可卸载。
4. reload 后 provider factory 目前是覆盖注册模型，尚未实现扩展移除后的回滚策略。

## 下一会话优先事项

1. Provider 兼容层：为非标准 Anthropic 网关补专用 adapter 或协议降级策略（避免空流）。
2. 插件隔离装载：`AssemblyLoadContext` + unload 路径 + reload 回收验证。
3. CLI 结构化输出：JSON/JSONL 事件流模式，便于脚本化与回归测试自动化。

## 编码约定

1. .NET 10，启用 nullable 与 implicit usings。
2. 文件作用域命名空间。
3. 异步方法命名以 `Async` 结尾，且带 `CancellationToken`。
4. 测试框架为 xUnit。
