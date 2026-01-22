# SharpAgent 项目目录结构

```
.
├── AGENTS.md
├── config.example.json
├── docs/
│   ├── agent_loop.md
│   ├── architecture.md
│   ├── configuration.md
│   ├── how_it_works.md
│   ├── skills.md
│   ├── skills_refs.md
│   ├── streaming-architecture.md
│   └── tools.md
├── LICENSE
├── README.md
├── Share.md
├── SharpAgent.Api/
│   ├── appsettings.Development.json
│   ├── appsettings.json
│   ├── Models/
│   │   ├── ChatModels.cs
│   │   └── UserChatModels.cs
│   ├── Program.cs
│   ├── Properties/
│   │   └── launchSettings.json
│   ├── Services/
│   │   ├── UserAgentConfig.cs
│   │   ├── UserAgentService.cs
│   │   ├── WeChatConfig.cs
│   │   └── WeChatWorkClient.cs
│   ├── SharpAgent.Api.csproj
│   ├── SharpAgent.Api.http
│   └── user_workspaces/
│       ├── 12755/
│       └── 14573/
├── SharpAgent.Console/
│   ├── HistoryTextPrompt.cs
│   ├── Program.cs
│   ├── SharpAgent.Console.csproj
│   └── ToolFormatters.cs
├── SharpAgent.Core/
│   ├── Agent.cs
│   ├── AgentOptions.cs
│   ├── AgentsMdLoader.cs
│   ├── AnthropicClient.cs
│   ├── Configuration/
│   │   ├── AgentConfig.cs
│   │   └── ConfigurationService.cs
│   ├── IAgent.cs
│   ├── ILlmClient.cs
│   ├── ITool.cs
│   ├── Message.cs
│   ├── OpenAiClient.cs
│   ├── Search/
│   │   ├── DuckDuckGoSearchClient.cs
│   │   └── ISearchClient.cs
│   ├── Sessions/
│   │   ├── ChatHistoryService.cs
│   │   ├── ISession.cs
│   │   ├── ISessionStore.cs
│   │   ├── JsonSessionStore.cs
│   │   ├── Session.cs
│   │   └── TimestampedMessage.cs
│   ├── SharpAgent.Core.csproj
│   ├── Skills/
│   │   ├── SkillMetadata.cs
│   │   └── SkillsLoader.cs
│   ├── Streaming/
│   │   ├── AgentEventEnvelope.cs
│   │   ├── AgentEvents.cs
│   │   ├── AgentStreamEvents.cs
│   │   ├── EventStream.cs
│   │   ├── IEventStore.cs
│   │   ├── IEventStream.cs
│   │   ├── NdjsonEventStore.cs
│   │   └── SubscriptionOptions.cs
│   └── Tools/
│       ├── BashTool.cs
│       ├── CalculatorTool.cs
│       ├── EditFileTool.cs
│       ├── GlobTool.cs
│       ├── GrepTool.cs
│       ├── ListFilesTool.cs
│       ├── ReadFileTool.cs
│       └── SearchTool.cs
├── SharpAgent.Core.Tests/
│   ├── AgentsMdLoaderTests.cs
│   ├── AgentTests.cs
│   ├── CalculatorToolTests.cs
│   ├── DuckDuckGoSearchClientTests.cs
│   ├── EditFileToolTests.cs
│   ├── GlobToolTests.cs
│   ├── GrepToolTests.cs
│   ├── ListFilesToolTests.cs
│   ├── ReadFileToolTests.cs
│   ├── SearchToolTests.cs
│   ├── SessionTests.cs
│   ├── SharpAgent.Core.Tests.csproj
│   └── SkillsLoaderTests.cs
├── SharpAgent.sln
├── SharpAgent.WinForms/
│   ├── ConfigDialog.cs
│   ├── ConfigDialog.Designer.cs
│   ├── Controls/
│   │   ├── ChatBubble.cs
│   │   ├── ChatPanel.cs
│   │   ├── ModernButton.cs
│   │   ├── ModernInputArea.cs
│   │   ├── RoundedTextBox.cs
│   │   ├── ThinkingCard.cs
│   │   └── ToolCallCard.cs
│   ├── MainForm.cs
│   ├── MainForm.Designer.cs
│   ├── MainForm.resx
│   ├── Program.cs
│   ├── Resources/
│   ├── SharpAgent.WinForms.csproj
│   └── Theme.cs
└── TODO.md

24 directories, 114 files
```

## 关键项目说明

1. **SharpAgent.Core** - 核心库，包含代理循环、LLM 客户端接口、工具抽象和消息类型
2. **SharpAgent.Core.Tests** - xUnit 测试，使用 NSubstitute 进行模拟
3. **SharpAgent.Console** - CLI 应用程序入口点
4. **SharpAgent.WinForms** - Windows Forms GUI 应用程序
5. **SharpAgent.Api** - Web API 服务，支持多用户
6. **docs/** - 项目文档和架构说明

## 主要接口

- `IAgent` - 代理接口
- `ILlmClient` - LLM 客户端接口
- `ITool` - 工具接口

## 技能支持

技能 (Skills) 位于 `SharpAgent.Core/Skills/` 目录中:
- `SkillMetadata` - 技能元数据模型
- `SkillsLoader` - 技能发现和解析器

技能搜索路径（按优先级）:
1. `.agents/skills/**` - 项目本地（递归）
2. `.claude/skills/*` - Claude Code 项目本地
3. `~/.claude/skills/*` - Claude Code 用户
4. `~/.codex/skills/**` - Codex CLI（递归）
5. `~/.config/agents/skills/*` - Agent Skills 标准

Agent 使用 `read_file` 工具读取 SKILL.md 文件加载技能指令。

## 工具位置

所有工具都位于 `SharpAgent.Core/Tools/` 目录中，并实现 `ITool` 接口。

## 构建和测试命令

- **构建**: `dotnet build`
- **全部测试**: `dotnet test`
- **单个测试**: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`
- **运行控制台**: `dotnet run --project SharpAgent.Console`

## 代码风格

- .NET 10, C# 启用可空引用类型和隐式 using
- 文件作用域命名空间 (`namespace X;`)
- 接口前缀为 `I` (如 `ITool`, `IAgent`)
- 私有字段使用 `_camelCase`
- 异步方法后缀为 `Async` 并接受 `CancellationToken ct = default`
- 不可变数据类型使用记录类型 (如 `Message`, `LlmResponse`)
- 测试使用 xUnit `[Fact]` 属性，命名方式为 `ClassName_Method_ExpectedBehavior`
- 测试中使用 NSubstitute 进行模拟
