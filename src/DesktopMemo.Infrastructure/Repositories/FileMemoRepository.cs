using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DesktopMemo.Core.Contracts;
using DesktopMemo.Core.Models;
using DesktopMemo.Infrastructure.Memos;

namespace DesktopMemo.Infrastructure.Repositories;

/// <summary>
/// 基于文件系统的 Markdown 备忘录存储实现。
/// </summary>
public sealed class FileMemoRepository : IMemoRepository
{
    private const string IndexFileName = "index.json";
    private readonly string _contentDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileMemoRepository(string dataDirectory)
    {
        _contentDirectory = Path.Combine(dataDirectory, "content");

        Directory.CreateDirectory(_contentDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true
        };
    }

    public async Task<IReadOnlyList<Memo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var memos = new List<Memo>(index.Order.Count);

        foreach (var memoId in index.Order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = GetMemoPath(memoId);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var memo = await LoadMemoAsync(path, cancellationToken).ConfigureAwait(false);
                if (memo != null)
                {
                    memos.Add(memo);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException)
            {
                // 跳过格式错误的文件，避免崩溃
                // 可以在这里添加日志记录：文件格式错误，跳过加载
                continue;
            }
        }

        return memos;
    }

    public async Task<Memo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = GetMemoPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await LoadMemoAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException)
        {
            // 文件格式错误，返回null
            return null;
        }
    }

    public async Task AddAsync(Memo memo, CancellationToken cancellationToken = default)
    {
        var path = GetMemoPath(memo.Id);
        await SaveMemoAsync(path, memo, cancellationToken).ConfigureAwait(false);

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!index.Order.Contains(memo.Id))
        {
            index.Order.Insert(0, memo.Id);
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateAsync(Memo memo, CancellationToken cancellationToken = default)
    {
        var path = GetMemoPath(memo.Id);
        await SaveMemoAsync(path, memo, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = GetMemoPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (index.Order.Remove(id))
        {
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Memo> LoadMemoAsync(string path, CancellationToken cancellationToken)
    {
        var document = await MemoMarkdownDocumentReader.ReadDocumentAsync(path, cancellationToken).ConfigureAwait(false);
        
        // 获取文件系统时间戳作为后备方案
        var fileInfo = new FileInfo(path);
        var fileCreatedAt = new DateTimeOffset(fileInfo.CreationTimeUtc);
        var fileUpdatedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
        
        var metadata = ParseMetadata(document.FrontMatter, fileCreatedAt, fileUpdatedAt);

        return new Memo(
            metadata.Id,
            metadata.Title,
            document.Content,
            BuildPreview(document.Content),
            metadata.CreatedAt,
            metadata.UpdatedAt,
            metadata.Tags,
            metadata.IsPinned);
    }

    private async Task SaveMemoAsync(string path, Memo memo, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"id: {memo.Id}");
        builder.AppendLine($"title: {MemoYamlScalarCodec.Encode(memo.Title)}");
        builder.AppendLine($"createdAt: {memo.CreatedAt.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"updatedAt: {memo.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"isPinned: {memo.IsPinned.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine("tags:");
        foreach (var tag in memo.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"  - {MemoYamlScalarCodec.Encode(tag)}");
        }
        builder.AppendLine("---");
        builder.Append(memo.Content);

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (trimmed.Length <= 120)
        {
            return trimmed;
        }

        var firstLineBreak = trimmed.IndexOf('\n');
        if (firstLineBreak >= 0 && firstLineBreak < 120)
        {
            return trimmed[..firstLineBreak];
        }

        return trimmed.Substring(0, 120) + "...";
    }

    private async Task<MemoIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_contentDirectory, IndexFileName);
        if (!File.Exists(path))
        {
            return new MemoIndex();
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MemoIndex>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new MemoIndex();
        }
        catch (JsonException)
        {
            // 索引文件格式错误，返回空索引
            return new MemoIndex();
        }
    }

    private async Task SaveIndexAsync(MemoIndex index, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_contentDirectory, IndexFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, index, _jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private string GetMemoPath(Guid id) => Path.Combine(_contentDirectory, $"{id:N}.md");

    private static MemoMetadata ParseMetadata(string yaml, DateTimeOffset fileCreatedAt, DateTimeOffset fileUpdatedAt)
    {
        var frontMatter = MemoMarkdownFrontMatterParser.Parse(yaml);
        var id = frontMatter.Id ?? Guid.NewGuid();

        // 如果 YAML front matter 中没有时间戳，使用文件系统时间作为后备方案
        var finalCreatedAt = frontMatter.CreatedAt ?? fileCreatedAt;
        var finalUpdatedAt = frontMatter.UpdatedAt ?? fileUpdatedAt;

        return new MemoMetadata(id, frontMatter.Title, finalCreatedAt, finalUpdatedAt, frontMatter.Tags, frontMatter.IsPinned);
    }

    private sealed class MemoIndex
    {
        public List<Guid> Order { get; set; } = new();
    }

    private sealed record MemoMetadata(
        Guid Id,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<string> Tags,
        bool IsPinned);
}

