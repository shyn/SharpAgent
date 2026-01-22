# How SharpAgent Works

SharpAgent is a powerful AI agent framework built with .NET 10. It implements a standard **Reasoning-Act (ReAct)** loop, allowing an LLM to use various tools to accomplish complex tasks.

## Table of Contents
1. [Architecture Overview](architecture.md)
2. [Agent Loop & Message Flow](agent_loop.md)
3. [Tool System](tools.md)
4. [Configuration & Extensibility](configuration.md)

## Core Philosophy
- **Modular**: Core logic is decoupled from UI and LLM providers.
- **Async & Streaming**: Designed for real-time interaction with streaming thought and text.
- **Type-Safe**: Leverages C# 10+ features for robust and maintainable code.

## Quick Start for Developers
The core entry point for the agent logic is the `Agent` class in `SharpAgent.Core`. It coordinates between the `ILlmClient` and various `ITool` implementations.

```csharp
var agent = new Agent(llmClient, tools);
await foreach (var evt in agent.RunStreamingAsync("Find all .cs files in the repo"))
{
    // Handle events (thinking, text delta, tool calls, etc.)
}
```
