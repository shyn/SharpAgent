# Agent Skills

SharpAgent supports the [Agent Skills specification](https://agentskills.io/specification), allowing you to extend the agent with domain-specific instructions and workflows.

## Overview

Skills are modular packages that provide specialized instructions for the agent. When the agent recognizes a task that matches an available skill, it can activate that skill using the `skill` tool to load detailed instructions.

## Skill Directory Structure

Skills are discovered from multiple locations (in priority order):

| Location | Recursive | Description |
|----------|-----------|-------------|
| `AgentOptions.SkillDirectories` | No | Custom directories |
| `.agents/skills/**` | Yes | Project-local skills |
| `.claude/skills/*` | No | Claude Code project-local |
| `~/.claude/skills/*` | No | Claude Code user skills |
| `~/.codex/skills/**` | Yes | Codex CLI user skills |
| `~/.config/agents/skills/*` | No | Agent Skills standard |

Each directory containing a `SKILL.md` file is treated as a skill:

```
~/.config/agents/skills/
├── pdf-processing/
│   └── SKILL.md
├── data-analysis/
│   ├── SKILL.md
│   ├── scripts/
│   └── references/
└── code-review/
    └── SKILL.md
```

For recursive locations (`.agents/skills`, `~/.codex/skills`), skills can be nested:

```
.agents/skills/
├── integrations/
│   ├── slack/
│   │   └── SKILL.md
│   └── github/
│       └── SKILL.md
└── utilities/
    └── pdf-tools/
        └── SKILL.md
```

## SKILL.md Format

Each skill must have a `SKILL.md` file with YAML frontmatter:

```markdown
---
name: pdf-processing
description: Extracts text and tables from PDF files, fills forms, merges documents. Use when working with PDF documents.
license: Apache-2.0
---

# PDF Processing Skill

Step-by-step instructions for working with PDFs...
```

### Required Fields

| Field | Description |
|-------|-------------|
| `name` | 1-64 characters, lowercase alphanumeric and hyphens only. Must match directory name. |
| `description` | 1-1024 characters. Describes what the skill does and when to use it. |

### Optional Fields

| Field | Description |
|-------|-------------|
| `license` | License name or reference to bundled license file |
| `compatibility` | Max 500 characters. Environment requirements |
| `metadata` | Key-value mapping for additional metadata |
| `allowed-tools` | Space-delimited list of pre-approved tools |

## Configuration

### AgentOptions

```csharp
var options = new AgentOptions
{
    WorkingDirectory = "/path/to/project",
    LoadSkills = true,  // Default: true
    SkillDirectories = new[]
    {
        "/path/to/custom/skills",
        "/another/skills/directory"
    }
};
```

### Disabling Skills

```csharp
var options = new AgentOptions
{
    LoadSkills = false
};
```

## How It Works

1. **Discovery**: At startup, the agent scans skill directories for valid `SKILL.md` files
2. **Metadata Injection**: Skill names, descriptions, and locations are injected into the system prompt as `<available_skills>` XML
3. **Activation**: When the agent recognizes a task matching a skill, it uses `read_file` to read the SKILL.md file
4. **Execution**: The agent follows the skill instructions to complete the task

This follows the "Filesystem-based agents" approach from the Agent Skills spec - no special tool needed, just `read_file`.

## Example Skill

Create a skill at `~/.config/agents/skills/git-workflow/SKILL.md`:

```markdown
---
name: git-workflow
description: Guides through Git workflows including branching, rebasing, and PR creation. Use when user asks about Git operations or needs help with version control.
---

# Git Workflow Skill

## Creating a Feature Branch

1. Ensure you're on the main branch: `git checkout main`
2. Pull latest changes: `git pull origin main`
3. Create feature branch: `git checkout -b feature/your-feature-name`

## Committing Changes

Follow conventional commits format:
- `feat: add new feature`
- `fix: resolve bug`
- `docs: update documentation`

...
```

## References

- [Agent Skills Specification](https://agentskills.io/specification)
- [Integration Guide](https://agentskills.io/integrate-skills)
