using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DesktopMemo.Infrastructure.Memos;

internal sealed record MemoFrontMatter(
    Guid? Id,
    string Title,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Tags,
    bool IsPinned);

internal static class MemoMarkdownFrontMatterParser
{
    public static MemoFrontMatter Parse(string yaml)
    {
        Guid? id = null;
        var title = string.Empty;
        DateTimeOffset? createdAt = null;
        DateTimeOffset? updatedAt = null;
        var isPinned = false;
        var tags = new List<string>();

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                if (Guid.TryParse(line[3..].Trim(), out var parsedId))
                {
                    id = parsedId;
                }
            }
            else if (line.StartsWith("title:", StringComparison.Ordinal))
            {
                title = MemoYamlScalarCodec.Decode(line[6..]);
            }
            else if (line.StartsWith("createdAt:", StringComparison.Ordinal))
            {
                if (DateTimeOffset.TryParse(
                    line[10..].Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedCreatedAt))
                {
                    createdAt = parsedCreatedAt;
                }
            }
            else if (line.StartsWith("updatedAt:", StringComparison.Ordinal))
            {
                if (DateTimeOffset.TryParse(
                    line[10..].Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsedUpdatedAt))
                {
                    updatedAt = parsedUpdatedAt;
                }
            }
            else if (line.StartsWith("isPinned:", StringComparison.Ordinal))
            {
                if (bool.TryParse(line[9..].Trim(), out var parsedIsPinned))
                {
                    isPinned = parsedIsPinned;
                }
            }
            else if (line.TrimStart().StartsWith("-", StringComparison.Ordinal))
            {
                var dashIndex = line.IndexOf('-');
                if (dashIndex >= 0)
                {
                    var tag = MemoYamlScalarCodec.Decode(line[(dashIndex + 1)..]);
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }
        }

        return new MemoFrontMatter(id, title, createdAt, updatedAt, tags, isPinned);
    }
}
