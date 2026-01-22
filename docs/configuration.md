# Configuration & Extensibility

SharpAgent is highly configurable to support various LLM providers and runtime behaviors.

## Configuration File (`config.json`)

The application looks for a `config.json` file in its working directory. A template is provided in `config.example.json`.

### Key Settings
- **`DefaultModel`**: The model to use if none is specified (e.g., `openai/gpt-4o-mini`).
- **`Providers`**: A list of LLM provider configurations.
    - `ApiKey`: Your secret key for the provider.
    - `BaseUrl`: The API endpoint.
    - `Models`: Specific capabilities and limits for each model.

## Dynamic Features

### Thinking Mode
For models that support it (like Anthropic Claude 3.7+), SharpAgent supports a dynamic "Thinking Mode". This allows the model to output its internal reasoning process before generating a final response. This can be toggled in the UI at runtime without restarting.

## Extensibility
You can extend SharpAgent by:
1.  **Adding Tools**: Creating new classes that implement `ITool`.
2.  **Adding Providers**: Implementing `ILlmClient` for a new API (e.g., Google Gemini, local Ollama).
3.  **Custom UI**: Use the `SharpAgent.Core` library to build your own interface.
