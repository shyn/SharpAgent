using System.Collections.Immutable;
using System.Text.Json;
using Sharp.AI;
using Sharp.Core.Sessions;

namespace Sharp.Core.Compaction;

/// <summary>
/// Tracks file operations (read, created, edited, deleted) from session entries.
/// Ported from pi-mono/packages/coding-agent/src/core/compaction/utils.ts
/// </summary>
public sealed record FileOperations
{
    /// <summary>
    /// Set of files that were read.
    /// </summary>
    public ImmutableHashSet<string> ReadFiles { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of files that were created.
    /// </summary>
    public ImmutableHashSet<string> CreatedFiles { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of files that were edited.
    /// </summary>
    public ImmutableHashSet<string> EditedFiles { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of files that were deleted.
    /// </summary>
    public ImmutableHashSet<string> DeletedFiles { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns all files that were touched (read, created, edited, or deleted).
    /// </summary>
    public IEnumerable<string> AllFiles => ReadFiles
        .Concat(CreatedFiles)
        .Concat(EditedFiles)
        .Concat(DeletedFiles)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a read file operation.
    /// </summary>
    public FileOperations WithReadFile(string path)
        => this with { ReadFiles = ReadFiles.Add(path) };

    /// <summary>
    /// Adds a created file operation.
    /// </summary>
    public FileOperations WithCreatedFile(string path)
        => this with { CreatedFiles = CreatedFiles.Add(path) };

    /// <summary>
    /// Adds an edited file operation.
    /// </summary>
    public FileOperations WithEditedFile(string path)
        => this with { EditedFiles = EditedFiles.Add(path) };

    /// <summary>
    /// Adds a deleted file operation.
    /// </summary>
    public FileOperations WithDeletedFile(string path)
        => this with { DeletedFiles = DeletedFiles.Add(path) };

    /// <summary>
    /// Merges two FileOperations records.
    /// </summary>
    public FileOperations Merge(FileOperations other)
        => new()
        {
            ReadFiles = ReadFiles.Union(other.ReadFiles),
            CreatedFiles = CreatedFiles.Union(other.CreatedFiles),
            EditedFiles = EditedFiles.Union(other.EditedFiles),
            DeletedFiles = DeletedFiles.Union(other.DeletedFiles)
        };
}

/// <summary>
/// Utility class for extracting and tracking file operations from session entries.
/// </summary>
public static class FileOperationTracker
{
    /// <summary>
    /// Tool names that indicate file read operations.
    /// </summary>
    private static readonly HashSet<string> ReadToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read",
        "read_file",
        "view",
        "view_file",
        "cat"
    };

    /// <summary>
    /// Tool names that indicate file write/create operations.
    /// </summary>
    private static readonly HashSet<string> WriteToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "write",
        "write_file",
        "create",
        "create_file"
    };

    /// <summary>
    /// Tool names that indicate file edit operations.
    /// </summary>
    private static readonly HashSet<string> EditToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "edit",
        "edit_file",
        "replace",
        "replace_in_file",
        "apply"
    };

    /// <summary>
    /// Tool names that indicate file delete operations.
    /// </summary>
    private static readonly HashSet<string> DeleteToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "delete",
        "delete_file",
        "rm",
        "remove"
    };

    /// <summary>
    /// Extracts file operations from a list of session entries.
    /// </summary>
    /// <param name="entries">The session entries to analyze.</param>
    /// <returns>A FileOperations record containing all tracked operations.</returns>
    public static FileOperations ExtractFileOperations(IEnumerable<SessionEntryEnvelope> entries)
    {
        var operations = new FileOperations();

        foreach (var entry in entries)
        {
            operations = operations.Merge(ExtractFromEntry(entry));
        }

        return operations;
    }

    /// <summary>
    /// Extracts file operations from a single session entry.
    /// </summary>
    /// <param name="entry">The session entry to analyze.</param>
    /// <returns>A FileOperations record containing operations from this entry.</returns>
    public static FileOperations ExtractFromEntry(SessionEntryEnvelope entry)
    {
        return entry.Type switch
        {
            "message" => ExtractFromMessageEntry(entry),
            "custom_message" => ExtractFromCustomMessageEntry(entry),
            _ => new FileOperations()
        };
    }

    private static FileOperations ExtractFromMessageEntry(SessionEntryEnvelope entry)
    {
        var operations = new FileOperations();

        try
        {
            var payload = entry.GetPayload<MessageEntryPayload>(JsonDefaults.Options);
            if (payload?.Message?.Content == null)
                return operations;

            foreach (var block in payload.Message.Content)
            {
                if (block is ToolCallContentBlock toolCall)
                {
                    operations = operations.Merge(ExtractFromToolCall(toolCall));
                }
                else if (block is ToolResultContentBlock toolResult)
                {
                    operations = operations.Merge(ExtractFromToolResult(toolResult));
                }
            }
        }
        catch
        {
            // Ignore deserialization errors
        }

        return operations;
    }

    private static FileOperations ExtractFromCustomMessageEntry(SessionEntryEnvelope entry)
    {
        var operations = new FileOperations();

        try
        {
            var payload = entry.GetPayload<CustomMessageEntryPayload>(JsonDefaults.Options);
            if (string.IsNullOrWhiteSpace(payload?.Content))
                return operations;

            // Try to extract file paths from the content text
            operations = operations.Merge(ExtractPathsFromText(payload.Content));
        }
        catch
        {
            // Ignore deserialization errors
        }

        return operations;
    }

    private static FileOperations ExtractFromToolCall(ToolCallContentBlock toolCall)
    {
        var operations = new FileOperations();
        var toolName = toolCall.ToolName;
        var arguments = toolCall.ArgumentsJson;

        if (string.IsNullOrWhiteSpace(arguments))
            return operations;

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            var root = doc.RootElement;

            // Extract path from common argument names
            if (root.TryGetProperty("path", out var pathElement) ||
                root.TryGetProperty("file_path", out pathElement) ||
                root.TryGetProperty("filepath", out pathElement) ||
                root.TryGetProperty("file", out pathElement))
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    operations = CategorizeOperation(operations, toolName, path);
                }
            }

            // Handle multiple paths (for batch operations)
            if (root.TryGetProperty("paths", out var pathsElement) && pathsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var pathItem in pathsElement.EnumerateArray())
                {
                    var path = pathItem.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        operations = CategorizeOperation(operations, toolName, path);
                    }
                }
            }
        }
        catch
        {
            // If JSON parsing fails, try regex extraction
            operations = operations.Merge(ExtractPathsFromText(arguments));
        }

        return operations;
    }

    private static FileOperations ExtractFromToolResult(ToolResultContentBlock toolResult)
    {
        // Tool results might contain file paths in error messages or content
        var operations = ExtractPathsFromText(toolResult.ContentText);

        // Also check the tool name
        if (!string.IsNullOrWhiteSpace(toolResult.ToolName))
        {
            // We don't know the exact operation from result alone,
            // but we can note the file was accessed
            if (ReadToolNames.Contains(toolResult.ToolName))
            {
                // Try to extract paths from the result content
                foreach (var path in ExtractFilePaths(toolResult.ContentText))
                {
                    operations = operations.WithReadFile(path);
                }
            }
        }

        return operations;
    }

    private static FileOperations CategorizeOperation(FileOperations operations, string toolName, string path)
    {
        if (ReadToolNames.Contains(toolName))
            return operations.WithReadFile(path);

        if (WriteToolNames.Contains(toolName))
            return operations.WithCreatedFile(path);

        if (EditToolNames.Contains(toolName))
            return operations.WithEditedFile(path);

        if (DeleteToolNames.Contains(toolName))
            return operations.WithDeletedFile(path);

        // Default: treat as read
        return operations.WithReadFile(path);
    }

    private static FileOperations ExtractPathsFromText(string text)
    {
        var operations = new FileOperations();

        if (string.IsNullOrWhiteSpace(text))
            return operations;

        // Simple heuristic: look for file paths in common patterns
        var paths = ExtractFilePaths(text);

        foreach (var path in paths)
        {
            operations = operations.WithReadFile(path);
        }

        return operations;
    }

    private static IEnumerable<string> ExtractFilePaths(string text)
    {
        var paths = new List<string>();

        // Look for common file path patterns
        // This is a simplified extraction - in practice, you might want more sophisticated parsing

        // Pattern: "path/to/file.ext" or 'path/to/file.ext'
        var inQuotes = false;
        var inSingleQuotes = false;
        var currentPath = new System.Text.StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '"' && !inSingleQuotes)
            {
                if (inQuotes && currentPath.Length > 0)
                {
                    var path = currentPath.ToString();
                    if (LooksLikeFilePath(path))
                        paths.Add(path);
                    currentPath.Clear();
                }
                inQuotes = !inQuotes;
                continue;
            }

            if (c == '\'' && !inQuotes)
            {
                if (inSingleQuotes && currentPath.Length > 0)
                {
                    var path = currentPath.ToString();
                    if (LooksLikeFilePath(path))
                        paths.Add(path);
                    currentPath.Clear();
                }
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (inQuotes || inSingleQuotes)
            {
                currentPath.Append(c);
            }
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFilePath(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Quick heuristics to identify file paths
        if (text.Contains('/') || text.Contains("\\"))
            return true;

        if (text.Contains('.'))
        {
            // Has file extension
            var parts = text.Split('.');
            if (parts.Length >= 2 && parts[^1].Length <= 10)
                return true;
        }

        return false;
    }
}
