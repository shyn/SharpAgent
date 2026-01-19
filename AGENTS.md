# SharpAgent

## Commands
- **Build**: `dotnet build`
- **Test all**: `dotnet test`
- **Test single**: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"` (e.g., `dotnet test --filter "FullyQualifiedName~AgentTests.Run_WithSimpleGoal_ReturnsLlmResponse"`)
- **Run console**: `dotnet run --project SharpAgent.Console`

## Architecture
- **SharpAgent.Core**: Core library with agent loop, LLM client interface, tool abstractions, and message types
- **SharpAgent.Core.Tests**: xUnit tests using NSubstitute for mocking
- **SharpAgent.Console**: CLI application entry point
- **SharpAgent.WinForms**: Windows Forms GUI application
- Key interfaces: `IAgent`, `ILlmClient`, `ITool`
- Tools live in `SharpAgent.Core/Tools/` and implement `ITool`

## Code Style
- .NET 10, C# with nullable enabled and implicit usings
- File-scoped namespaces (`namespace X;`)
- Interfaces prefixed with `I` (e.g., `ITool`, `IAgent`)
- Private fields use `_camelCase`
- Async methods suffixed with `Async` and accept `CancellationToken ct = default`
- Use records for immutable data types (e.g., `Message`, `LlmResponse`)
- Tests use xUnit `[Fact]` attributes with `ClassName_Method_ExpectedBehavior` naming
- Use NSubstitute for mocking in tests
