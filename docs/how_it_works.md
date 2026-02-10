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
   - Extension runtime lifecycle hooks (`ExtensionRuntime`)
   - Context compaction primitives (`CompactionService`, compaction entries)

3. `Sharp.Core.Tests`
   - Unit + integration validation

## Core Principle

Provider transport concerns live in `Sharp.AI`; orchestration and persistence live in `Sharp.Core`.

This keeps agent behavior testable without coupling loop logic to wire protocols.

## Current Non-Goals

- No TUI/Web/Desktop host.
- No extension isolation/unload via `AssemblyLoadContext` yet.
- No automatic compaction wiring in `AgentSession.PromptAsync/ContinueAsync` yet.
