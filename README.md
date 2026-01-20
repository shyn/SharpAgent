# SharpAgent

[English](#english) | [简体中文](#简体中文)

---

## English

### Overview

SharpAgent is a powerful AI agent framework built with .NET 10 and C#. It provides a flexible architecture for building intelligent agents that can reason, use tools, and interact with various LLM (Large Language Model) providers like OpenAI and Anthropic.

### Key Features

- **Streaming Architecture**: Real-time response streaming with thinking process visibility
- **Tool System**: Extensible tool framework with built-in utilities (calculator, file operations, bash commands, etc.)
- **Multi-Provider Support**: Works with OpenAI, Anthropic, and compatible API providers
- **Multiple Interfaces**: 
  - Console application (CLI) with interactive chat
  - Windows Forms GUI application
- **Async/Streaming**: Full async support with streaming events for real-time feedback

### Project Structure

```
SharpAgent/
├── SharpAgent.Core/          # Core library containing the agent logic
│   ├── Agent.cs              # Main agent implementation with streaming support
│   ├── Configuration/        # Configuration management
│   ├── Streaming/            # Streaming event types
│   └── Tools/                # Built-in tool implementations
│       ├── CalculatorTool.cs
│       ├── ReadFileTool.cs
│       ├── ListFilesTool.cs
│       ├── BashTool.cs
│       ├── GlobTool.cs
│       ├── GrepTool.cs
│       └── EditFileTool.cs
├── SharpAgent.Core.Tests/    # xUnit tests with NSubstitute mocking
├── SharpAgent.Console/       # CLI application entry point
├── SharpAgent.WinForms/      # Windows Forms GUI application
└── docs/                     # Documentation
```

### Architecture

SharpAgent follows a clean, modular architecture:

- **IAgent**: Core interface for running agent tasks
- **ILlmClient**: Abstraction for LLM providers (OpenAI, Anthropic)
- **ITool**: Interface for extending agent capabilities with tools
- **Streaming Events**: Real-time feedback during agent execution

### Built-in Tools

| Tool | Description |
|------|-------------|
| `calculator` | Mathematical expression evaluation |
| `read_file` | Read file contents |
| `list_files` | List directory contents |
| `bash` | Execute bash commands |
| `glob` | Find files by pattern |
| `grep` | Search text patterns in files |
| `edit_file` | Edit file contents |

### Requirements

- .NET 10.0 SDK or later
- API key for your chosen LLM provider (OpenAI or Anthropic)

### Installation & Setup

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd SharpAgent
   ```

2. Copy the example configuration:
   ```bash
   cp config.example.json config.json
   ```

3. Configure your API key in `config.json` or set environment variables:
   ```bash
   export OPENAI_API_KEY="your-api-key"
   export ANTHROPIC_API_KEY="your-api-key"
   ```

### Usage

#### Building the Project

```bash
dotnet build
```

#### Running the Console Application

```bash
dotnet run --project SharpAgent.Console
```

#### Running the Windows Forms GUI

```bash
dotnet run --project SharpAgent.WinForms
```

#### Running Tests

Run all tests:
```bash
dotnet test
```

Run a specific test:
```bash
dotnet test --filter "FullyQualifiedName~AgentTests.Run_WithSimpleGoal_ReturnsLlmResponse"
```

### Configuration

The application uses `config.json` for configuration. Key settings include:

```json
{
  "defaultModel": "openai/gpt-4o",
  "providers": [
    {
      "id": "openai",
      "apiKey": null,
      "baseUrl": "https://api.openai.com/v1/",
      "models": [...]
    },
    {
      "id": "anthropic",
      "apiKey": null,
      "baseUrl": "https://api.anthropic.com/v1/",
      "models": [...]
    }
  ]
}
```

API keys can be set via:
- Environment variables (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`)
- Directly in `config.json` (not recommended for production)

### Code Style

- .NET 10, C# with nullable reference types enabled
- File-scoped namespaces
- Interfaces prefixed with `I` (e.g., `ITool`, `IAgent`)
- Private fields use `_camelCase`
- Async methods suffixed with `Async` and accept `CancellationToken ct = default`
- Records for immutable data types
- xUnit tests with `ClassName_Method_ExpectedBehavior` naming convention
- NSubstitute for mocking in tests

### License

MIT License - see LICENSE file for details.

---

## 简体中文

### 概述

SharpAgent 是一个基于 .NET 10 和 C# 构建的强大 AI 代理框架。它提供了灵活的架构，可用于构建智能代理，这些代理能够进行推理、使用工具并与各种 LLM（大型语言模型）提供商（如 OpenAI 和 Anthropic）交互。

### 主要特性

- **流式架构**：支持实时响应流，并可显示思考过程
- **工具系统**：可扩展的工具框架，包含内置实用工具（计算器、文件操作、bash 命令等）
- **多提供商支持**：支持 OpenAI、Anthropic 和兼容的 API 提供商
- **多种界面**：
  - 控制台应用程序（CLI），支持交互式聊天
  - Windows 窗体 GUI 应用程序
- **异步/流式支持**：完整的异步支持，流式事件提供实时反馈

### 项目结构

```
SharpAgent/
├── SharpAgent.Core/          # 包含代理逻辑的核心库
│   ├── Agent.cs              # 具有流式支持的主要代理实现
│   ├── Configuration/        # 配置管理
│   ├── Streaming/            # 流式事件类型
│   └── Tools/                # 内置工具实现
│       ├── CalculatorTool.cs
│       ├── ReadFileTool.cs
│       ├── ListFilesTool.cs
│       ├── BashTool.cs
│       ├── GlobTool.cs
│       ├── GrepTool.cs
│       └── EditFileTool.cs
├── SharpAgent.Core.Tests/    # 使用 NSubstitute 模拟的 xUnit 测试
├── SharpAgent.Console/       # CLI 应用程序入口点
├── SharpAgent.WinForms/      # Windows 窗体 GUI 应用程序
└── docs/                     # 文档
```

### 架构

SharpAgent 采用简洁、模块化的架构：

- **IAgent**：用于运行代理任务的核心接口
- **ILlmClient**：LLM 提供商的抽象（OpenAI、Anthropic）
- **ITool**：用于通过工具扩展代理能力的接口
- **流式事件**：代理执行期间的实时反馈

### 内置工具

| 工具 | 描述 |
|------|-------------|
| `calculator` | 数学表达式求值 |
| `read_file` | 读取文件内容 |
| `list_files` | 列出目录内容 |
| `bash` | 执行 bash 命令 |
| `glob` | 按模式查找文件 |
| `grep` | 在文件中搜索文本模式 |
| `edit_file` | 编辑文件内容 |

### 环境要求

- .NET 10.0 SDK 或更高版本
- 所选 LLM 提供商的 API 密钥（OpenAI 或 Anthropic）

### 安装与配置

1. 克隆仓库：
   ```bash
   git clone <仓库-url>
   cd SharpAgent
   ```

2. 复制示例配置：
   ```bash
   cp config.example.json config.json
   ```

3. 在 `config.json` 中配置 API 密钥，或设置环境变量：
   ```bash
   export OPENAI_API_KEY="your-api-key"
   export ANTHROPIC_API_KEY="your-api-key"
   ```

### 使用方法

#### 构建项目

```bash
dotnet build
```

#### 运行控制台应用程序

```bash
dotnet run --project SharpAgent.Console
```

#### 运行 Windows 窗体 GUI

```bash
dotnet run --project SharpAgent.WinForms
```

#### 运行测试

运行所有测试：
```bash
dotnet test
```

运行特定测试：
```bash
dotnet test --filter "FullyQualifiedName~AgentTests.Run_WithSimpleGoal_ReturnsLlmResponse"
```

### 配置

应用程序使用 `config.json` 进行配置。主要设置包括：

```json
{
  "defaultModel": "openai/gpt-4o",
  "providers": [
    {
      "id": "openai",
      "apiKey": null,
      "baseUrl": "https://api.openai.com/v1/",
      "models": [...]
    },
    {
      "id": "anthropic",
      "apiKey": null,
      "baseUrl": "https://api.anthropic.com/v1/",
      "models": [...]
    }
  ]
}
```

API 密钥可通过以下方式设置：
- 环境变量（`OPENAI_API_KEY`、`ANTHROPIC_API_KEY`）
- 直接在 `config.json` 中设置（生产环境不推荐）

### 代码规范

- .NET 10，支持可空引用类型的 C#
- 文件作用域命名空间
- 接口以 `I` 为前缀（如 `ITool`、`IAgent`）
- 私有字段使用 `_camelCase`
- 异步方法以 `Async` 为后缀，并接受 `CancellationToken ct = default`
- 使用 record 表示不可变数据类型
- xUnit 测试使用 `类名_方法名_预期行为` 命名约定
- 测试中使用 NSubstitute 进行模拟

### 许可证

MIT 许可证 - 详见 LICENSE 文件。
