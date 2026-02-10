# How It Works

SharpAgent currently targets a **headless library runtime**.

## Layers

1. `Sharp.AI`
   - Provider adapters
   - Message/content abstractions
   - Stream event contracts

2. `Sharp.Core`
   - Session lifecycle (`AgentSession`)
   - Agent loop (`AgentLoop`)
   - Tool execution (`ToolRuntime`)
   - JSONL tree sessions (`SessionManager`)

3. `Sharp.Core.Tests`
   - Unit + integration validation

## Core Principle

Provider transport concerns live in `Sharp.AI`; orchestration and persistence live in `Sharp.Core`.

This keeps agent behavior testable without coupling loop logic to wire protocols.

## Current Non-Goals

- No TUI/Web/Desktop host.
- No extension runtime.
- No compaction pipeline yet.
