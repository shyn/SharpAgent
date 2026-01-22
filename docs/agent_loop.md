# Agent Loop & Message Flow

SharpAgent uses a **ReAct (Reasoning + Acting)** pattern to solve problems.

## The Execution Loop

When `RunStreamingAsync` is called, the following steps occur:

1.  **Initialization**: A conversation history is started with a `System` message (the persona) and a `User` message (the goal).
2.  **LLM Call**: The current history and available tool definitions are sent to the `ILlmClient`.
3.  **Streaming Response**:
    - **Thinking**: If the model supports it (like Claude), the agent streams its internal "thoughts".
    - **Text**: The agent's verbal response is streamed to the user.
    - **Tool Calls**: If the LLM determines a tool is needed, it returns one or more `ToolCall` requests.
4.  **Action**: If tool calls were received:
    - The agent executes the requested tools locally.
    - The results (success or error) are added to the conversation history as `Tool` messages.
    - The loop returns to Step 2 to let the LLM analyze the results.
5.  **Completion**: If the LLM returns text without any tool calls, the agent assumes the task is finished and yields a completion event.

## Message Types
- **System**: Defines the agent's behavior and constraints.
- **User**: The task or subsequent instructions from the human.
- **Assistant**: The LLM's responses (including thinking and tool calls).
- **Tool**: The output from a tool execution.

## Iteration Limit
To prevent "infinite loops" where an agent gets stuck or repeats actions, a `maxIterations` (default 20) is enforced.
