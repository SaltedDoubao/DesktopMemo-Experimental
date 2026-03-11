using System;
using System.IO;
using System.Threading.Tasks;
using DesktopMemo.Infrastructure.Memos;
using DesktopMemo.Infrastructure.Repositories;
using Xunit;

namespace DesktopMemo.Infrastructure.Tests;

public sealed class MemoFrontMatterParsingTests
{
    [Fact]
    public async Task ReadFrontMatterAndParse_ReturnsExplicitTimestamps()
    {
        using var tempDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "memo.md");

        await File.WriteAllTextAsync(filePath, """
            ---
            id: 7cbfdb90-1d74-4332-b2e9-4757830bd33d
            title: 时间测试
            createdAt: 2026-03-10T19:44:47.4679167+08:00
            updatedAt: 2026-03-10T19:44:50.6879260+08:00
            isPinned: True
            tags:
              - alpha
              - beta
            ---
            body
            """);

        var yaml = await MemoMarkdownDocumentReader.ReadFrontMatterAsync(filePath);
        var frontMatter = MemoMarkdownFrontMatterParser.Parse(yaml);

        Assert.Equal(Guid.Parse("7cbfdb90-1d74-4332-b2e9-4757830bd33d"), frontMatter.Id);
        Assert.Equal("时间测试", frontMatter.Title);
        Assert.Equal(DateTimeOffset.Parse("2026-03-10T19:44:47.4679167+08:00"), frontMatter.CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-03-10T19:44:50.6879260+08:00"), frontMatter.UpdatedAt);
        Assert.True(frontMatter.IsPinned);
        Assert.Equal(["alpha", "beta"], frontMatter.Tags);
    }

    [Fact]
    public async Task ReadFrontMatterAndParse_RoundTripsEscapedYamlScalars()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repository = new FileMemoRepository(tempDirectory.Path);
        var memo = new DesktopMemo.Core.Models.Memo(
            Guid.NewGuid(),
            "标题: \"引号\"\n第二行\\路径",
            "body",
            "body",
            DateTimeOffset.Parse("2026-03-10T19:44:47.4679167+08:00"),
            DateTimeOffset.Parse("2026-03-10T19:44:50.6879260+08:00"),
            ["tag:one", "tag\"two", "tag\nthree", "path\\tag"],
            false);

        await repository.AddAsync(memo);

        var filePath = Path.Combine(tempDirectory.Path, "content", $"{memo.Id:N}.md");
        var yaml = await MemoMarkdownDocumentReader.ReadFrontMatterAsync(filePath);
        var parsed = MemoMarkdownFrontMatterParser.Parse(yaml);

        Assert.Contains("title: \"标题: \\\"引号\\\"\\n第二行\\\\路径\"", yaml);
        Assert.Contains("- \"tag\\nthree\"", yaml);
        Assert.Equal(memo.Title, parsed.Title);
        Assert.Equal(memo.Tags, parsed.Tags);
    }

    [Fact]
    public async Task ReadFrontMatterAsync_Throws_WhenFrontMatterExceedsLimit()
    {
        using var tempDirectory = new TemporaryDirectory();
        var filePath = Path.Combine(tempDirectory.Path, "memo.md");
        var oversizedLine = new string('a', MemoMarkdownDocumentReader.MaxFrontMatterLength + 1);

        await File.WriteAllTextAsync(filePath, $$"""
            ---
            title: "{{oversizedLine}}"
            ---
            body
            """);

        await Assert.ThrowsAsync<InvalidDataException>(() => MemoMarkdownDocumentReader.ReadFrontMatterAsync(filePath));
    }
}
