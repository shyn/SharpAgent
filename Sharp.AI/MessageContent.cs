using System.Text;

namespace Sharp.AI;

public static class MessageContent
{
    public static string FlattenText(IReadOnlyList<ContentBlock> content)
    {
        var builder = new StringBuilder();

        foreach (var block in content)
        {
            switch (block)
            {
                case TextContentBlock text:
                    builder.Append(text.Text);
                    break;
                case ThinkingContentBlock thinking:
                    builder.Append(thinking.Text);
                    break;
                case ToolResultContentBlock toolResult:
                    builder.Append(toolResult.ContentText);
                    break;
                case ImageContentBlock:
                    builder.Append("[image]");
                    break;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<ToolCallContentBlock> GetToolCalls(IReadOnlyList<ContentBlock> content)
        => content.OfType<ToolCallContentBlock>().ToList();
}
