# Configuration

TinyClaw uses a single `settings.json` file for all configuration. The file is stored in:

**Windows**: `C:\ProgramData\TinyClaw\settings.json`

## Configuration File Structure

```json
{
  "workspace": {
    "path": "C:\\Projects\\TinyClaw",
    "name": "My Workspace"
  },
  "channels": {
    "enabled": ["discord", "telegram"],
    "discord": {
      "bot_token": "YOUR_DISCORD_BOT_TOKEN"
    },
    "telegram": {
      "bot_token": "YOUR_TELEGRAM_BOT_TOKEN",
      "proxy_url": "http://proxy.company.com:8080"
    }
  },
  "models": {
    "provider": "anthropic",
    "api_keys": {
      "anthropic": "YOUR_ANTHROPIC_API_KEY",
      "openai": "YOUR_OPENAI_API_KEY"
    },
    "anthropic": {
      "model": "sonnet"
    },
    "openai": {
      "model": "gpt-4o"
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
  "teams": {},
  "monitoring": {
    "heartbeat_interval": 3600
  }
}
```

## Sections

### Workspace

Defines the workspace location for agent file operations.

```json
{
  "workspace": {
    "path": "C:\\Projects\\TinyClaw",
    "name": "My Workspace"
  }
}
```

| Property | Description |
|----------|-------------|
| `path` | Root directory for all agent working directories |
| `name` | Display name for the workspace |

### Channels

Configure Discord and Telegram bot integrations.

#### Discord

```json
{
  "channels": {
    "discord": {
      "bot_token": "YOUR_BOT_TOKEN"
    }
  }
}
```

#### Telegram

```json
{
  "channels": {
    "telegram": {
      "bot_token": "YOUR_BOT_TOKEN",
      "proxy_url": "http://proxy:8080"
    }
  }
}
```

| Property | Required | Description |
|----------|----------|-------------|
| `bot_token` | Yes | Bot token from @BotFather |
| `proxy_url` | No | HTTP/SOCKS proxy for API calls |

### Models

Configure the AI providers and API authentication.

```json
{
  "models": {
    "provider": "anthropic",
    "api_keys": {
      "anthropic": "sk-ant-api...",
      "openai": "sk-..."
    },
    "anthropic": {
      "model": "sonnet"
    },
    "openai": {
      "model": "gpt-4o"
    }
  }
}
```

| Property | Description |
|----------|-------------|
| `provider` | Default provider: `"anthropic"` or `"openai"` |
| `api_keys` | API keys for each provider (can also use env vars) |
| `anthropic.model` | Claude model: `sonnet`, `opus`, or specific model ID |
| `openai.model` | OpenAI model: `gpt-4o`, `gpt-4-turbo`, etc. |

#### API Key Priority

API keys are resolved in this order:
1. Agent-specific `api_key` in agent config
2. Global `api_keys` in models config
3. Environment variables (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`)

### Agents

Define custom agents with advanced options:

```json
{
  "agents": {
    "my-agent": {
      "name": "My Agent",
      "provider": "anthropic",
      "model": "sonnet",
      "working_directory": "my-agent",
      "api_key": "optional-override-key",
      "base_url": "https://api.anthropic.com/",
      "thinking_level": "medium",
      "max_turns": 20,
      "system_prompt": "Optional system prompt override",
      "allow_write_outside_workspace": false,
      "session_id": "my-session-id",
      "enable_extensions": true
    }
  }
}
```

| Property | Required | Description |
|----------|----------|-------------|
| `name` | Yes | Display name for the agent |
| `provider` | Yes | `"anthropic"` or `"openai"` |
| `model` | Yes | Model identifier |
| `working_directory` | Yes | Directory for agent file operations |
| `api_key` | No | Override API key for this agent |
| `base_url` | No | Custom API endpoint URL |
| `thinking_level` | No | `off`, `minimal`, `low`, `medium`, `high`, `xhigh` |
| `max_turns` | No | Maximum turns per conversation (default: 20) |
| `system_prompt` | No | Override system prompt (skips SOUL.md) |
| `allow_write_outside_workspace` | No | Allow file writes outside working dir (default: false) |
| `session_id` | No | Persist conversations to specific session |
| `enable_extensions` | No | Enable Sharp.Core extensions (default: true) |

### Thinking Levels

For models that support thinking/reasoning:

| Level | Description |
|-------|-------------|
| `off` | No thinking tokens |
| `minimal` | Minimal reasoning |
| `low` | Low amount of reasoning |
| `medium` | Moderate reasoning |
| `high` | Extensive reasoning |
| `xhigh` | Maximum reasoning (may use more tokens) |

### Teams

Define agent teams. See [Teams](teams.md) for details.

### Monitoring

```json
{
  "monitoring": {
    "heartbeat_interval": 3600
  }
}
```

| Property | Description |
|----------|-------------|
| `heartbeat_interval` | Health check interval in seconds (0 to disable) |

#### Heartbeat Mechanism

The heartbeat periodically sends a status check message to all agents to keep them active and responsive.

**Default behavior:**
- Interval: 3600 seconds (1 hour)
- Message: `"@{agentId} Quick status check: Any pending tasks? Keep response brief."`
- Sent to all configured agents

**Custom heartbeat prompt:**

Create a `heartbeat.md` file in the agent's working directory to customize the message:

```
{workspace}/{agentId}/heartbeat.md
```

Example `heartbeat.md`:
```markdown
Check for any incomplete tasks, review your todo list, and report status. Keep it brief.
```

**Configuration examples:**

```json
// Default: heartbeat every hour
{
  "monitoring": { "heartbeat_interval": 3600 }
}

// Frequent: every 5 minutes
{
  "monitoring": { "heartbeat_interval": 300 }
}

// Disabled
{
  "monitoring": { "heartbeat_interval": 0 }
}
```

## Environment Variables

You can use environment variables for sensitive values:

```bash
# Windows
set ANTHROPIC_API_KEY=your_key
set OPENAI_API_KEY=your_key
set TINYLAW_DISCORD_TOKEN=your_token
set TINYLAW_TELEGRAM_TOKEN=your_token

# PowerShell
$env:ANTHROPIC_API_KEY="your_key"
$env:OPENAI_API_KEY="your_key"
```

### API Key Environment Variables

| Provider | Environment Variable |
|----------|---------------------|
| Anthropic | `ANTHROPIC_API_KEY` |
| OpenAI | `OPENAI_API_KEY` |

## Proxy Configuration

For environments behind corporate firewalls:

### HTTP Proxy
```json
{
  "telegram": {
    "proxy_url": "http://proxy.company.com:8080"
  }
}
```

### Authenticated Proxy
```json
{
  "telegram": {
    "proxy_url": "http://user:password@proxy.company.com:8080"
  }
}
```

### SOCKS5 Proxy
```json
{
  "telegram": {
    "proxy_url": "socks5://proxy.company.com:1080"
  }
}
```

## Session Persistence

TinyClaw uses Sharp.Core's session management to persist conversation history. Sessions are stored as JSONL files in:

```
{workspace}/.sessions/{session_id}.jsonl
```

Each agent gets its own session file (based on `session_id` config or auto-generated). This enables:
- Conversation continuity across restarts
- Tree-structured conversation branching
- Context window management with automatic compaction

## Configuration via WPF UI

The WPF application provides a graphical interface for common settings:

1. Launch `TinyClaw.App.exe`
2. Navigate to **Settings** page
3. Configure:
   - Channel tokens (Discord, Telegram)
   - API keys
   - Workspace path
   - Default AI provider and model
   - Heartbeat interval
   - Telegram proxy URL
4. Click **Save Settings**

## Configuration via CLI

```bash
# Set Discord token
TinyClaw.Cli.exe config set channels.discord.bot_token YOUR_TOKEN

# Set Telegram token
TinyClaw.Cli.exe config set channels.telegram.bot_token YOUR_TOKEN

# Set API keys
TinyClaw.Cli.exe config set models.api_keys.anthropic YOUR_KEY
TinyClaw.Cli.exe config set models.api_keys.openai YOUR_KEY

# Set proxy
TinyClaw.Cli.exe config set channels.telegram.proxy_url http://proxy:8080

# View current config
TinyClaw.Cli.exe config get
```
