using Sharp.AI;

namespace Sharp.Core.Extensions;

public interface IAgentExtension
{
    string Name { get; }

    ValueTask InitializeAsync(IAgentExtensionApi api, CancellationToken ct = default);
}

public interface IAgentExtensionApi
{
    void OnInput(ExtensionInputHandler handler);

    void OnContext(ExtensionContextHandler handler);

    void OnBeforeAgentStart(ExtensionBeforeAgentStartHandler handler);

    void OnToolCall(ExtensionToolCallHandler handler);

    void OnToolResult(ExtensionToolResultHandler handler);

    void OnSessionStart(ExtensionSessionStartHandler handler);

    void OnSessionBeforeSwitch(ExtensionSessionBeforeSwitchHandler handler);

    void OnSessionSwitch(ExtensionSessionSwitchHandler handler);

    void OnSessionBeforeFork(ExtensionSessionBeforeForkHandler handler);

    void OnSessionFork(ExtensionSessionForkHandler handler);

    void OnSessionBeforeTree(ExtensionSessionBeforeTreeHandler handler);

    void OnSessionTree(ExtensionSessionTreeHandler handler);

    void OnSessionBeforeCompact(ExtensionSessionBeforeCompactHandler handler);

    void OnSessionCompact(ExtensionSessionCompactHandler handler);

    void OnSessionShutdown(ExtensionSessionShutdownHandler handler);

    void OnResourcesDiscover(ExtensionResourcesDiscoverHandler handler);

    void RegisterTool(ExtensionToolDefinition tool);

    void RegisterCommand(ExtensionCommandDefinition command);

    void RegisterFlag(ExtensionFlagDefinition flag);

    string? GetFlag(string name);

    void RegisterProviderFactory(ProviderApiKind apiKind, Func<LlmProviderCreateContext, ILlmProvider> factory, bool overwrite = true);
}
