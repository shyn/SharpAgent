# TinyClaw Documentation

Welcome to TinyClaw - a multi-channel AI agent orchestration platform powered by Sharp.Core.

## Overview

TinyClaw allows you to create and manage AI agents that can respond to messages from Discord, Telegram, and other channels. Agents can work individually or in teams to process incoming messages.

**Powered by Sharp.Core**: TinyClaw uses Sharp.Core as its agent runtime, providing:
- Direct LLM API integration (no CLI dependencies)
- Streaming response handling
- Session persistence with JSONL storage
- Tool execution (read, write, edit, bash, grep, find, ls)
- Automatic context window management
- Extension system support

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        TinyClaw                              │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │ WPF App  │  │  CLI     │  │ Service  │  │  Core    │    │
│  │   (UI)   │  │(Commands)│  │(Windows) │  │(Shared)  │    │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘    │
│       └─────────────┴─────────────┴─────────────┘           │
│                          │                                  │
│                    ┌─────┴─────┐                            │
│                    │  SQLite   │                            │
│                    │  Queue    │                            │
│                    └─────┬─────┘                            │
│                          │                                  │
│       ┌──────────────────┼──────────────────┐               │
│       │                  │                  │               │
│  ┌────┴────┐       ┌────┴────┐       ┌────┴────┐           │
│  │ Discord │       │Telegram │       │Sharp.AI│            │
│  │  Bot    │       │  Bot    │       │Providers│           │
│  └─────────┘       └─────────┘       └─────────┘           │
│                                                            │
└─────────────────────────────────────────────────────────────┘
```

## Components

| Component | Description |
|-----------|-------------|
| `TinyClaw.App` | WPF desktop application for management UI |
| `TinyClaw.Cli` | Command-line interface for scripting |
| `TinyClaw.Service` | Windows Service for background processing |
| `TinyClaw.Core` | Shared library using Sharp.Core for agent runtime |
| `Sharp.Core` | Agent runtime (session, tools, extensions) |
| `Sharp.AI` | LLM providers (Anthropic, OpenAI) |

## Key Features

### Direct LLM Integration
Unlike earlier versions that required CLI tools (`claude` or `codex`), TinyClaw now integrates directly with LLM APIs via Sharp.Core:
- No CLI dependencies required
- Better performance with streaming support
- Session persistence built-in
- Automatic retry and error handling

### Multi-Channel Support
- **Discord**: Text, files, DMs, mentions
- **Telegram**: Text, files, photos, voice, documents
- Extensible architecture for adding new channels

### Agent Management
- Multiple specialized agents with isolated workspaces
- Team-based collaboration with leader/worker pattern
- Per-agent configuration (model, thinking level, max turns)
- Session persistence across restarts

### Tools
Agents have access to these tools via Sharp.Core:
- `read` - Read files
- `write` - Write files
- `edit` - Edit files with patches
- `bash` - Execute shell commands
- `grep` - Search file contents
- `find` - Find files by name
- `ls` - List directory contents

## Quick Links

- [Configuration](configuration.md) - Settings and API keys
- [Agents](agents.md) - Creating and managing agents
- [Teams](teams.md) - Working with agent teams
- [Channels](channels.md) - Discord and Telegram integration
- [Deployment](deployment.md) - Installing and running TinyClaw

## Configuration Example

```json
{
  "workspace": {
    "path": "C:\\TinyClawWorkspace"
  },
  "models": {
    "api_keys": {
      "anthropic": "sk-ant-api...",
      "openai": "sk-..."
    },
    "anthropic": {
      "model": "sonnet"
    }
  },
  "agents": {
    "default": {
      "name": "Default",
      "provider": "anthropic",
      "model": "sonnet",
      "working_directory": "default",
      "thinking_level": "medium",
      "max_turns": 20
    }
  },
  "channels": {
    "discord": {
      "bot_token": "YOUR_DISCORD_TOKEN"
    },
    "telegram": {
      "bot_token": "YOUR_TELEGRAM_TOKEN"
    }
  }
}
```

## Getting Started

1. **Configure API Keys**: Add your Anthropic and/or OpenAI API keys
2. **Set up Channels**: Configure Discord and/or Telegram bot tokens
3. **Define Agents**: Create agents for your use cases
4. **Deploy**: Install as Windows Service or run standalone

See [Deployment](deployment.md) for detailed setup instructions.
