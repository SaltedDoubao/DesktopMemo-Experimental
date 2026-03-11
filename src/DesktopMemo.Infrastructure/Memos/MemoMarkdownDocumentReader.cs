using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopMemo.Infrastructure.Memos;

internal sealed record MemoMarkdownDocument(string FrontMatter, string Content);

internal static class MemoMarkdownDocumentReader
{
    public static async Task<MemoMarkdownDocument> ReadDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await ReadCoreAsync(path, includeContent: true, cancellationToken).ConfigureAwait(false);
        return new MemoMarkdownDocument(result.FrontMatter, result.Content ?? string.Empty);
    }

    public static async Task<string> ReadFrontMatterAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await ReadCoreAsync(path, includeContent: false, cancellationToken).ConfigureAwait(false);
        return result.FrontMatter;
    }

    private static async Task<(string FrontMatter, string? Content)> ReadCoreAsync(
        string path,
        bool includeContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var firstLine = await reader.ReadLineAsync().ConfigureAwait(false);
        if (firstLine is null || !firstLine.Equals("---", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"备忘录文件 {path} 缺少 front matter。");
        }

        var metadataBuilder = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (line.Equals("---", StringComparison.Ordinal))
            {
                var content = includeContent
                    ? await reader.ReadToEndAsync().ConfigureAwait(false)
                    : null;

                return (metadataBuilder.ToString(), content);
            }

            metadataBuilder.AppendLine(line);
        }

        throw new InvalidDataException($"备忘录文件 {path} front matter 未正确结束。");
    }
}
