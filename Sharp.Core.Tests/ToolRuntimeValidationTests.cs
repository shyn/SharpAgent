using System.Text.Json;
using Sharp.AI;

namespace Sharp.Core.Tests;

public sealed class ToolRuntimeValidationTests
{
    [Fact]
    public async Task ExecuteAsync_EnumViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "enum-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "mode": { "type": "string", "enum": ["strict", "relaxed"] }
              },
              "required": ["mode"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"mode\":\"unknown\"}"));

        Assert.True(result.IsError);
        Assert.Contains("enum values", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_OneOfViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "oneof-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "target": {
                  "oneOf": [
                    { "type": "string" },
                    { "type": "integer" }
                  ]
                }
              },
              "required": ["target"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"target\":true}"));

        Assert.True(result.IsError);
        Assert.Contains("oneOf", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_NestedArrayItemsViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "nested-array-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "id": { "type": "string" }
                    },
                    "required": ["id"]
                  }
                }
              },
              "required": ["items"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"items\":[{\"id\":\"ok\"},{}]}"));

        Assert.True(result.IsError);
        Assert.Contains("$.items[1]", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_OneOfAndNestedItemsValid_InvokesTool()
    {
        var tool = new SchemaTool(
            "complex-valid",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "target": {
                  "oneOf": [
                    { "type": "string" },
                    { "type": "integer" }
                  ]
                },
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "id": { "type": "string" }
                    },
                    "required": ["id"]
                  }
                }
              },
              "required": ["target", "items"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"target\":123,\"items\":[{\"id\":\"a\"}]}"));

        Assert.False(result.IsError);
        Assert.Equal("ok", result.ContentAsText);
        Assert.True(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_AnyOfViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "anyof-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "payload": {
                  "anyOf": [
                    { "type": "string" },
                    { "type": "integer" }
                  ]
                }
              },
              "required": ["payload"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"payload\":true}"));

        Assert.True(result.IsError);
        Assert.Contains("anyOf", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_AllOfViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "allof-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "name": {
                  "allOf": [
                    { "type": "string" },
                    { "const": "allowed" }
                  ]
                }
              },
              "required": ["name"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"name\":\"blocked\"}"));

        Assert.True(result.IsError);
        Assert.Contains("const value", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_NotViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "not-check",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "mode": {
                  "type": "string",
                  "not": { "const": "dangerous" }
                }
              },
              "required": ["mode"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"mode\":\"dangerous\"}"));

        Assert.True(result.IsError);
        Assert.Contains("must not match", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_AnyOfAllOfNotValid_InvokesTool()
    {
        var tool = new SchemaTool(
            "combinator-valid",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "name": {
                  "allOf": [
                    { "type": "string" },
                    { "not": { "const": "forbidden" } }
                  ]
                },
                "payload": {
                  "anyOf": [
                    { "type": "string" },
                    { "type": "integer" }
                  ]
                }
              },
              "required": ["name", "payload"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"name\":\"safe\",\"payload\":\"ok\"}"));

        Assert.False(result.IsError);
        Assert.Equal("ok", result.ContentAsText);
        Assert.True(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_StringLengthPatternAndFormatViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "string-constraints",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "name": { "type": "string", "minLength": 3, "maxLength": 6, "pattern": "^[a-z]+$" },
                "email": { "type": "string", "format": "email" },
                "website": { "type": "string", "format": "uri" },
                "time": { "type": "string", "format": "date-time" }
              },
              "required": ["name", "email", "website", "time"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"name\":\"AB\",\"email\":\"bad\",\"website\":\"not-uri\",\"time\":\"bad\"}"));

        Assert.True(result.IsError);
        Assert.Contains("$.name", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_NumberRangeViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "number-range",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "score": { "type": "number", "minimum": 1, "maximum": 10 },
                "strict": { "type": "number", "exclusiveMinimum": 1, "exclusiveMaximum": 10 }
              },
              "required": ["score", "strict"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"score\":0,\"strict\":1}"));

        Assert.True(result.IsError);
        Assert.Contains("must be >=", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_ArrayAndObjectSizeViolation_ReturnsValidationError()
    {
        var tool = new SchemaTool(
            "container-size",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 2,
              "maxProperties": 3,
              "properties": {
                "items": { "type": "array", "minItems": 2, "maxItems": 3 },
                "name": { "type": "string" }
              },
              "required": ["items"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"items\":[1]}"));

        Assert.True(result.IsError);
        Assert.Contains("at least", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_ExtendedConstraintsValid_InvokesTool()
    {
        var tool = new SchemaTool(
            "extended-valid",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "minProperties": 2,
              "maxProperties": 4,
              "properties": {
                "name": { "type": "string", "minLength": 3, "maxLength": 8, "pattern": "^[a-z]+$" },
                "email": { "type": "string", "format": "email" },
                "score": { "type": "number", "minimum": 0, "maximum": 100 },
                "items": { "type": "array", "minItems": 1, "maxItems": 3 }
              },
              "required": ["name", "email", "score", "items"]
            }
            """);
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"name\":\"alice\",\"email\":\"a@example.com\",\"score\":88,\"items\":[1]}"));

        Assert.False(result.IsError);
        Assert.Equal("ok", result.ContentAsText);
        Assert.True(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredArgument_ReturnsValidationError()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{}"));

        Assert.True(result.IsError);
        Assert.Contains("missing required argument 'name'", result.ContentAsText, StringComparison.OrdinalIgnoreCase);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_TypeMismatch_ReturnsValidationError()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"name\":123}"));

        Assert.True(result.IsError);
        Assert.Contains("does not match expected type 'string'", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsParseError()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "{\"name\":\"alice\""));

        Assert.True(result.IsError);
        Assert.Contains("parse failed", result.ContentAsText, StringComparison.OrdinalIgnoreCase);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_NonObjectJson_ReturnsParseError()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(new ToolCall("call-1", tool.Name, "[\"alice\"]"));

        Assert.True(result.IsError);
        Assert.Contains("JSON object", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_AdditionalPropertiesBlocked_ReturnsValidationError()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"name\":\"alice\",\"extra\":true}"));

        Assert.True(result.IsError);
        Assert.Contains("Unknown argument 'extra'", result.ContentAsText, StringComparison.Ordinal);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ExecuteAsync_ValidArguments_InvokesTool()
    {
        var tool = new RecordingTool();
        var runtime = new ToolRuntime([tool]);

        var result = await runtime.ExecuteAsync(
            new ToolCall("call-1", tool.Name, "{\"name\":\"alice\",\"count\":2}"));

        Assert.False(result.IsError);
        Assert.Equal("ok", result.ContentAsText);
        Assert.True(tool.Executed);
    }

    private sealed class RecordingTool : IAgentTool
    {
        public string Name => "record";
        public string Description => "record";
        public bool Executed { get; private set; }

        public JsonElement ParametersSchema => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                name = new { type = "string" },
                count = new { type = "integer" }
            },
            required = new[] { "name" }
        }, JsonDefaults.Options);

        public Task<ToolInvocationResult> ExecuteAsync(
            JsonElement arguments,
            ToolExecutionContext context,
            IProgress<ToolInvocationResult>? progress = null,
            CancellationToken ct = default)
        {
            Executed = true;
            return Task.FromResult(ToolInvocationResult.Text("ok"));
        }
    }

    private sealed class SchemaTool : IAgentTool
    {
        public SchemaTool(string name, string schemaJson)
        {
            Name = name;
            using var doc = JsonDocument.Parse(schemaJson);
            ParametersSchema = doc.RootElement.Clone();
        }

        public string Name { get; }
        public string Description => "schema-tool";
        public bool Executed { get; private set; }
        public JsonElement ParametersSchema { get; }

        public Task<ToolInvocationResult> ExecuteAsync(
            JsonElement arguments,
            ToolExecutionContext context,
            IProgress<ToolInvocationResult>? progress = null,
            CancellationToken ct = default)
        {
            Executed = true;
            return Task.FromResult(ToolInvocationResult.Text("ok"));
        }
    }
}
