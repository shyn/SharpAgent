# SharpAgent.Core Code Review

**Date:** 2026-01-23  
**Scope:** SharpAgent.Core library  
**Effort estimate:** L (1–2 days) for full refactor; M (1–3h) for highest-risk fixes only

---

## TL;DR

SharpAgent.Core is a solid "works-first" implementation with sensible primitives (Agent loop, ILlmClient, ITool, streaming events), but it currently mixes concerns, relies heavily on "stringly-typed" error flows, and has a few correctness + maintainability hazards (sync-over-async in constructors, HttpClient disposal/creation patterns, inconsistent event models, and weak parsing/validation around tool calls and SSE streaming).

The biggest improvements come from:
- (a) separating orchestration vs prompt-building vs tool-execution
- (b) making errors typed/structured
- (c) tightening async/cancellation + HTTP lifecycle patterns

---

## Recommended Approach (Simple Path)

A minimal, high-leverage sequence that improves quality without "rewriting the library":

### 1. Fix sync-over-async in `Agent` construction

Replace `AgentsMdLoader.LoadAsync(...).GetAwaiter().GetResult()` and `SkillsLoader.DiscoverAsync(...).GetAwaiter().GetResult()` with:
- either an `Agent.CreateAsync(...)` factory, or
- lazy async initialization invoked at `Run*Async` start.

This avoids deadlocks, startup stalls, and threadpool starvation in some hosting environments.

### 2. Normalize error handling: stop encoding errors as `"Error: ..."` strings

Introduce a small result type:
```csharp
record ToolExecutionResult(bool IsError, string Output, string? ErrorCode = null, Exception? Exception = null);
```
or `OneOf<string, ToolError>` / discriminated union style.

Have tools return structured errors; only stringify at the boundary where you serialize back into LLM tool-result messages.

### 3. Make HTTP client lifecycle correct (and DI-friendly)

`OpenAiClient` / `AnthropicClient` currently `Dispose()` the injected `HttpClient`. That's usually wrong when `HttpClient` comes from `IHttpClientFactory`.

Options:
- Remove `IDisposable` from `ILlmClient`, or
- Keep it but **do not dispose** externally-owned `HttpClient` (use a flag like `_ownsHttpClient`).

In `ConfigurationService.CreateLlmClient`, stop creating raw `new HttpClient()` per call for real apps; instead provide `HttpClient` from factory at composition root.

### 4. Tighten cancellation + streaming robustness

Ensure all internal async enumerables respect `ct` all the way down (they mostly do; the bigger issue is partial swallowing of parse errors).

In SSE parsing (`AnthropicClient.ReadSseStreamAsync` and `OpenAiClient` line parsing), handle multi-line SSE "data:" segments and event framing properly or at least detect and log malformed frames.

### 5. Unify / clean event models

You have both `SharpAgent.Core.Streaming.AgentEvents.cs` (records like `AgentStarted`, `AgentCompleted`) and also `AgentStreamEvent` types referenced in `Agent.cs` (`AgentStartedEvent`, `AgentCompletedEvent`, etc.). This looks inconsistent and likely redundant/bug-prone.

Pick one event model and delete the other, or clearly separate "public streaming protocol" vs "internal event DTOs".

### 6. Add 3–5 targeted tests to lock behavior

- Agent loop: "tool call -> tool result -> follow-up completion".
- Tool error propagation is marked `IsError=true`.
- Streaming: given chunk sequence, emits correct event ordering and `LlmMessageCompletedEvent` data.

---

## Rationale and Trade-offs

- The current code is not "awful"; it's pragmatic and readable. The main quality issue is **missing boundaries**: prompt building, skills discovery, tool execution, event emission, and LLM streaming are all intertwined. That raises coupling and makes it hard to test any part in isolation.
- The "stringly-typed" error approach (`result.StartsWith("Error:")`) is a major maintainability trap: it's easy to break silently (localization, formatting changes) and makes structured handling impossible.
- The HTTP + disposal pattern is a classic .NET pitfall and can cause socket exhaustion or subtle runtime issues depending on hosting.

---

## Detailed Findings

### A. Code Organization / Separation of Concerns

**Findings:**
- `Agent` constructor does I/O (loads AGENTS.md, discovers skills) synchronously via `.GetResult()`.
- `Agent` is responsible for:
  - prompt composition,
  - conversation state mutation,
  - tool dispatch,
  - streaming event translation,
  - session persistence hook (`newMessagesTracker` + `AgentMessagesEvent`).

**Guardrails:**
- Extract:
  - `ISystemPromptBuilder` (base prompt + AGENTS.md + skills prompt)
  - `IToolExecutor` (maps tool name -> tool, executes, returns structured result)
  - `IAgentLoop` / `AgentRunner` that orchestrates "LLM -> tool -> LLM" and emits events.

### B. Interface Design / Abstraction Quality

**Findings:**
- `ITool.ParametersSchema` is `object`: flexible, but untyped and easy to mismatch across providers.
- `ILlmClient` blends "non-streaming completion" and "streaming completion" but returns different shapes (`LlmResponse` vs events). Some clients implement non-streaming by consuming streaming (`AnthropicClient.GetCompletionAsync`), others call REST directly (`OpenAiClient.GetCompletionAsync`)—behavior differences are likely.
- `ILlmClient : IDisposable` conflicts with typical DI patterns.

**Guardrails:**
- Keep `object` schema for now (fine), but standardize as `JsonElement` or `BinaryData` to avoid accidental serialization surprises.
- Align `GetCompletionAsync` semantics across providers (either both call streaming or both call non-streaming endpoints).

### C. Error Handling Patterns

**Findings:**
- Tools catch exceptions and return `"Error: ..."` strings; `Agent` re-infers errors by string prefix.
- `OpenAiClient.ProcessStreamChunkAsync` swallows JSON deserialization errors silently (`catch { yield break; }`), which can silently truncate completions and produce confusing agent behavior.
- `ConfigurationService.Load()` swallows all exceptions with no logging and silently resets to defaults—hard to debug.

**Guardrails:**
- Introduce structured error results; log parse errors at least at Debug/Warning with enough context (chunk size/type), without dumping secrets.

### D. Async/Await Usage and Cancellation Tokens

**Findings:**
- **Sync-over-async** in `Agent` ctor is the biggest async smell.
- `ExecuteToolAsync` properly passes `ct`.
- `BashTool` uses a linked CTS and kills the process on timeout—good. But it does not kill the process on external cancellation (`ct`) explicitly; `WaitForExitAsync` throws, but you don't distinguish timeout vs user cancellation except the timeout filter. External cancellation should likely also kill the process tree.

**Guardrails:**
- In `BashTool`, on `OperationCanceledException` (not timeout), also kill process and rethrow or return "cancelled" explicitly.
- Consider honoring cancellation in file tools: already pass `ct` to `ReadAllTextAsync`/`WriteAllTextAsync`—good.

### E. Naming Conventions and C# Best Practices

**Findings:**
- Generally good naming and use of records.
- `AgentEvents.cs` looks like an older/alternate API that conflicts with the newer `*Event` types.
- `Message` uses `Thinking` and `ToolCalls` with optional fields. That's convenient, but can become a "god record" over time.

**Guardrails:**
- If you keep a single `Message` record, consider helper constructors/factories:
  - `Message.System(string)`, `Message.User(string)`, `Message.AssistantText(...)`, `Message.ToolResult(...)`
  - This reduces invalid combinations (e.g., Tool role without ToolCallId).

### F. Potential Bugs / Code Smells

1. **Tool dictionary collisions**
   - `_toolsByName = _tools.ToDictionary(t => t.Name);` throws if two tools share a name.
   - Add validation with a clearer error early.

2. **Thinking handling inconsistency in Agent**
   - In the loop you track `thinkingBuilder` from deltas, but also store `thinking` from `LlmMessageCompletedEvent` and then ignore it.
   - If the provider emits only "completed thinking" (or your code misses some deltas), `thinkingBuilder` could be incomplete. Use one source of truth: prefer `completed.FullThinking` when available.

3. **SSE parsing fragility**
   - Anthropic SSE can include multiple `data:` lines per event; your reader assumes one `data:` line after an `event:` line. That can break with larger payloads.
   - OpenAI SSE parsing assumes `data: ` line-by-line JSON. Usually OK, but still should handle blank lines properly and tolerate partial frames.

4. **Logging sensitive data**
   - Both clients log full request/response bodies at Debug, and Anthropic also prints to Console on error. These bodies can include user content, tool outputs, and API behavior details.
   - In an agent system, tool output may include secrets from files/env. Logging it verbatim is risky.

5. **ConfigurationService + HttpClient creation**
   - `CreateLlmClient` creates new `HttpClient` instances; if consumers call it frequently, this is wasteful and can exhaust sockets.
   - Also mixes responsibilities (config + DI + client instantiation).

### G. Testability Concerns

**Findings:**
- Agent behavior depends on static loaders (`AgentsMdLoader`, `SkillsLoader`) invoked in ctor; hard to unit test without touching filesystem.
- `BashTool` uses `Process` directly; hard to test without integration tests.
- Clients do real HTTP unless you inject handlers; possible but not explicit.

**Guardrails:**
- Introduce injectable abstractions:
  - `IFileSystem` (or minimal wrapper) for file tools + config service.
  - `IProcessRunner` for BashTool.
  - Use `HttpMessageHandler` injection in tests for LLM clients (standard pattern).

---

## When to Consider the Advanced Path

Move beyond the "simple path" refactors if any of these become true:
- You need to support **multiple concurrent subscribers** and durable replay semantics with strict ordering guarantees (your `EventStream` is close, but would need stronger invariants).
- You want provider-agnostic support for **images, thinking/reasoning controls, JSON mode**, etc. The current `Message`/tool schema abstraction will start to strain.
- You need robust SSE handling across providers and versions (multi-line frames, retries, resumability).

---

## Optional Advanced Path (Outline Only)

- Define a canonical internal protocol:
  - `ChatTurn` state machine + typed events (`TextDelta`, `ThinkingDelta`, `ToolCallDelta`, `ToolCallCompleted`, `TurnCompleted`).
- Implement provider adapters that map provider-native streaming → canonical events.
- Implement a single agent loop engine that consumes canonical events, executes tools, and persists session state.

---

## Summary

| Area | Status | Priority |
|------|--------|----------|
| Sync-over-async in constructor | ✅ Fixed | High |
| Stringly-typed error handling | ✅ Fixed | High |
| HttpClient disposal pattern | ✅ Fixed | High |
| Duplicate event models | 🟡 Confusing | Medium |
| SSE parsing fragility | 🟡 Edge case bugs | Medium |
| Logging sensitive data | 🟡 Security risk | Medium |
| Agent class does too much | 🟡 Coupling | Medium |
| Tool name collision handling | 🟢 Minor | Low |
| Testability abstractions | 🟢 Nice to have | Low |

**Strengths:** readable code, reasonable use of async streams, clear data models (`Message`, `ToolCall`), straightforward agent loop.

**Weak points:** conflated responsibilities, brittle error signaling, HTTP lifecycle pitfalls, inconsistent event definitions, fragile SSE parsing, and risky logging.
