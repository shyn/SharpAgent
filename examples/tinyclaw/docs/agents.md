# Agents

Agents are the core AI workers in TinyClaw. Each agent is configured with a specific AI provider (Claude or OpenAI), model, and working directory. Agents run directly using Sharp.Core (not CLI processes), providing better performance and session management.

## Agent Configuration

### Default Agent

By default, TinyClaw creates a single "default" agent if no agents are configured. The default agent uses:
- Provider: Anthropic (Claude) or OpenAI based on settings
- Model: "sonnet" (Claude) or "gpt-4o" (OpenAI)
- Working Directory: `{workspace}/default`

### Custom Agents

You can define custom agents in `settings.json`:

```json
{
  "agents": {
    "frontend": {
      "name": "Frontend Developer",
      "provider": "anthropic",
      "model": "sonnet",
      "working_directory": "projects/frontend",
      "thinking_level": "medium",
      "max_turns": 20
    },
    "backend": {
      "name": "Backend Developer", 
      "provider": "openai",
      "model": "gpt-4o",
      "working_directory": "projects/backend",
      "thinking_level": "off",
      "max_turns": 25
    },
    "devops": {
      "name": "DevOps Engineer",
      "provider": "anthropic", 
      "model": "opus",
      "working_directory": "infrastructure",
      "thinking_level": "high",
      "max_turns": 30
    }
  }
}
```

### Agent Properties

| Property | Required | Description |
|----------|----------|-------------|
| `name` | Yes | Display name for the agent |
| `provider` | Yes | `"anthropic"` or `"openai"` |
| `model` | Yes | Model identifier (see below) |
| `working_directory` | Yes | Directory for agent file operations |
| `api_key` | No | Override API key for this agent |
| `base_url` | No | Custom API endpoint |
| `thinking_level` | No | `off`, `minimal`, `low`, `medium`, `high`, `xhigh` |
| `max_turns` | No | Max tool turns per conversation (default: 20) |
| `system_prompt` | No | Override system prompt (skips SOUL.md) |
| `allow_write_outside_workspace` | No | Allow writes outside working dir (default: false) |
| `session_id` | No | Persist conversations to specific session ID |
| `enable_extensions` | No | Enable Sharp.Core extensions (default: true) |

## Supported Models

### Anthropic (Claude)

| Model ID | Description | Context Window |
|----------|-------------|----------------|
| `sonnet` | Claude Sonnet 4.5 - balanced performance | 200K |
| `opus` | Claude Opus 4.6 - most capable | 200K |
| `haiku` | Claude 3.5 Haiku - fast, lightweight | 200K |
| `sonnet-3-5` | Claude 3.5 Sonnet | 200K |

### OpenAI

| Model ID | Description | Context Window |
|----------|-------------|----------------|
| `gpt-4o` | GPT-4o - multimodal, capable | 128K |
| `gpt-4-turbo` | GPT-4 Turbo | 128K |
| `gpt-4` | GPT-4 base | 8K |
| `gpt-3.5-turbo` | GPT-3.5 Turbo | 16K |

## Using Agents

### Direct Messaging

To route a message to a specific agent, prefix your message with `@agent_id`:

```
@frontend Create a React component for a login form
```

```
@backend Implement a JWT authentication middleware
```

### Without Agent Prefix

Messages without an `@agent_id` prefix are routed to the "default" agent.

## Agent Working Directory

Each agent operates within its own working directory:

- **Absolute paths**: Used as-is
- **Relative paths**: Resolved relative to the workspace root

Example structure:
```
C:\ProgramData\TinyClaw\workspace\
├── default\           # Default agent
├── projects\
│   ├── frontend\      # Frontend agent
│   └── backend\       # Backend agent
└── infrastructure\     # DevOps agent
```

## Creating Specialized Agents

### Code Review Agent
```json
{
  "code-reviewer": {
    "name": "Code Reviewer",
    "provider": "anthropic",
    "model": "opus",
    "working_directory": "reviews",
    "thinking_level": "high",
    "max_turns": 30
  }
}
```

### Documentation Agent
```json
{
  "docs": {
    "name": "Documentation Writer",
    "provider": "anthropic",
    "model": "sonnet",
    "working_directory": "docs",
    "thinking_level": "medium"
  }
}
```

### Testing Agent
```json
{
  "qa": {
    "name": "QA Engineer",
    "provider": "openai",
    "model": "gpt-4o",
    "working_directory": "tests",
    "thinking_level": "medium"
  }
}
```

## Agent Commands

Users can interact with agents using special commands:

| Command | Description |
|---------|-------------|
| `!agent` or `/agent` | List available agents |
| `!team` or `/team` | List available teams |
| `!reset` or `/reset` | Reset conversation context |

## Best Practices

1. **Separate Concerns**: Create different agents for different tasks (frontend, backend, devops)

2. **Working Directories**: Keep agent workspaces isolated to prevent file conflicts

3. **Model Selection**: 
   - Use `sonnet` for general tasks (faster, cheaper)
   - Use `opus` for complex reasoning tasks
   - Use `gpt-4o` for OpenAI with good balance
   - Use `haiku` for quick, simple tasks

4. **Thinking Level**:
   - `off` for simple, straightforward tasks
   - `medium` for most coding tasks
   - `high` or `xhigh` for complex architecture decisions

5. **Naming**: Use descriptive agent IDs (e.g., `frontend-dev` instead of `agent1`)

6. **Provider Consistency**: Agents in a team should ideally use the same provider for consistent behavior

## Session Management

TinyClaw uses Sharp.Core's session management to persist conversations. Each agent maintains its own session:

```
{workspace}/.sessions/{session_id}.jsonl
```

### Session Features

- **Persistence**: Conversations survive service restarts
- **Tree Structure**: Support for conversation branching/forking
- **Compaction**: Automatic context window management
- **Continuity**: Resume long-running conversations

### Resetting Conversations

Use `!reset` or `/reset` to clear an agent's conversation history and start fresh.

## Agent Personality (SOUL.md)

Define agent personality, behavior, and system instructions using a `SOUL.md` file.

### Location

```
{workspace}/{agentId}/.tinyclaw/SOUL.md
```

### Example SOUL.md

```markdown
# Frontend Developer Agent

You are an expert frontend developer specializing in React and TypeScript.

## Responsibilities
- Create responsive, accessible UI components
- Write clean, maintainable TypeScript code
- Follow modern React patterns (hooks, functional components)

## Communication Style
- Be concise but thorough
- Provide code examples when helpful
- Ask clarifying questions when requirements are unclear

## Tools & Technologies
- React 18+, TypeScript, Vite
- Tailwind CSS for styling
- React Query for data fetching

## Constraints
- Always use TypeScript (no JavaScript)
- Prefer functional components over classes
- Write tests for complex logic
```

### How it works

- SOUL.md is loaded when an agent session is created
- Content is used as the system prompt for the LLM
- Defines agent's persona, expertise, and constraints
- Applies to all messages processed by the agent
- Can be overridden by `system_prompt` in agent config

### Creating custom SOUL.md

1. Navigate to agent's `.tinyclaw/` directory:
   ```
   {workspace}/{agentId}/.tinyclaw/
   ```

2. Edit or create `SOUL.md`

3. Changes apply to new conversations (existing sessions keep old prompt)

### Best practices

- **Be specific** about the agent's role and expertise
- **Define constraints** (what the agent should/shouldn't do)
- **Set communication style** (formal, casual, technical, etc.)
- **Include context** about tools and technologies
- **Keep it concise** (LLMs have token limits for system prompts)

## Agent Heartbeat

TinyClaw can periodically send heartbeat messages to agents to keep them active or trigger scheduled tasks.

### How it works

- A heartbeat message is sent to all agents at a configurable interval (default: 1 hour)
- The message appears as a system message with `@agent_id` prefix
- Agents process heartbeats like normal messages

### Default heartbeat message

```
@{agentId} Quick status check: Any pending tasks? Keep response brief.
```

### Custom heartbeat prompt

Create a `heartbeat.md` file in the agent's working directory:

```
{workspace}/{agentId}/heartbeat.md
```

Example content:
```markdown
Review your current tasks and report any blockers. 
Check for overdue items and prioritize accordingly.
```

### Configuration

```json
{
  "monitoring": {
    "heartbeat_interval": 3600
  }
}
```

| Value | Behavior |
|-------|----------|
| `3600` | Heartbeat every hour (default) |
| `300` | Heartbeat every 5 minutes |
| `0` | Disable heartbeat |

### Use cases

- **Keep agents warm**: Prevent cold-start latency on infrequent agents
- **Periodic self-checks**: Agents review their own status
- **Scheduled tasks**: Trigger recurring maintenance or reporting
- **Health monitoring**: Detect unresponsive agents

## Troubleshooting

### Agent not responding
- Check if the agent is properly configured in settings
- Verify API keys are configured (settings or environment variables)
- Check service logs for errors
- Ensure the provider service is accessible

### Working directory issues
- Ensure the service has write permissions to the workspace
- Use absolute paths if relative paths don't resolve correctly
- Check that `allow_write_outside_workspace` is enabled if needed

### Model errors
- Verify the model ID is correct for the provider
- Check that your API keys are valid and have credits
- Verify `base_url` if using a custom endpoint

### Session issues
- Check `{workspace}/.sessions/` directory exists and is writable
- Review session JSONL files for corruption
- Use `!reset` to clear stuck sessions
