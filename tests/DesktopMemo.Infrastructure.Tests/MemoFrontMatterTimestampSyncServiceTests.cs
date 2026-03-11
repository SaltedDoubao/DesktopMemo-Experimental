using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DesktopMemo.Core.Contracts;
using DesktopMemo.Core.Models;
using DesktopMemo.Infrastructure.Repositories;
using DesktopMemo.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopMemo.Infrastructure.Tests;

public sealed class MemoFrontMatterTimestampSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_UpdatesSqliteTimestamps_WhenMarkdownFrontMatterDiffers()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var initialCreatedAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var initialUpdatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");
        var syncedCreatedAt = DateTimeOffset.Parse("2026-03-01T08:00:00+08:00");
        var syncedUpdatedAt = DateTimeOffset.Parse("2026-03-11T22:30:00+08:00");

        await repository.AddAsync(CreateMemo(id, initialCreatedAt, initialUpdatedAt));
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), BuildMarkdown(id, syncedCreatedAt, syncedUpdatedAt));

        var result = await service.SyncAsync();
        var reloaded = await repository.GetByIdAsync(id);

        Assert.NotNull(reloaded);
        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(syncedCreatedAt, reloaded!.CreatedAt);
        Assert.Equal(syncedUpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task SyncAsync_DoesNotUpdate_WhenMarkdownMatchesSqlite()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var updatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");

        await repository.AddAsync(CreateMemo(id, createdAt, updatedAt));

        var result = await service.SyncAsync();

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.ParseFailureCount);
    }

    [Fact]
    public async Task SyncAsync_SkipsFile_WhenUpdatedAtIsInvalid()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var updatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");

        await repository.AddAsync(CreateMemo(id, createdAt, updatedAt));
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), $$"""
            ---
            id: {{id}}
            title: 无效时间
            createdAt: 2026-03-01T08:00:00+08:00
            updatedAt: invalid
            isPinned: False
            tags:
            ---
            body
            """);

        var result = await service.SyncAsync();
        var dbRow = await ReadDatabaseRecordAsync(tempDirectory.Path, id);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.ParseFailureCount);
        Assert.NotNull(dbRow);
        Assert.Equal(createdAt.ToString("o"), dbRow!.Value.CreatedAt);
        Assert.Equal(updatedAt.ToString("o"), dbRow.Value.UpdatedAt);
    }

    [Fact]
    public async Task SyncAsync_SkipsFile_WhenMarkdownIsMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        await repository.AddAsync(CreateMemo(
            id,
            DateTimeOffset.Parse("2026-03-10T10:00:00+08:00"),
            DateTimeOffset.Parse("2026-03-10T10:05:00+08:00")));

        File.Delete(GetMemoPath(tempDirectory.Path, id));

        var result = await service.SyncAsync();

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.ParseFailureCount);
    }

    [Fact]
    public async Task SyncAsync_UsesFallbackPath_AndSelfHealsFilePath()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var updatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");
        var syncedUpdatedAt = DateTimeOffset.Parse("2026-03-12T11:15:00+08:00");

        await repository.AddAsync(CreateMemo(id, createdAt, updatedAt));
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), BuildMarkdown(id, createdAt, syncedUpdatedAt));
        await SetFilePathAsync(tempDirectory.Path, id, Path.Combine(tempDirectory.Path, "content", "stale.md"));

        var result = await service.SyncAsync();
        var dbRow = await ReadDatabaseRecordAsync(tempDirectory.Path, id);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.NotNull(dbRow);
        Assert.Equal(GetMemoPath(tempDirectory.Path, id), dbRow!.Value.FilePath);
        Assert.Equal(syncedUpdatedAt.ToString("o"), dbRow.Value.UpdatedAt);
    }

    [Fact]
    public async Task SyncAsync_SkipsFile_WhenFrontMatterIdDoesNotMatchDatabaseRow()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var updatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");

        await repository.AddAsync(CreateMemo(id, createdAt, updatedAt));
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), BuildMarkdown(otherId, createdAt.AddDays(-1), updatedAt.AddDays(1)));

        var result = await service.SyncAsync();
        var dbRow = await ReadDatabaseRecordAsync(tempDirectory.Path, id);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.NotNull(dbRow);
        Assert.Equal(createdAt.ToString("o"), dbRow!.Value.CreatedAt);
        Assert.Equal(updatedAt.ToString("o"), dbRow.Value.UpdatedAt);
    }

    [Fact]
    public async Task SyncAsync_SkipsFile_WhenFrontMatterExceedsLimit()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        await repository.AddAsync(CreateMemo(
            id,
            DateTimeOffset.Parse("2026-03-10T10:00:00+08:00"),
            DateTimeOffset.Parse("2026-03-10T10:05:00+08:00")));

        var oversizedLine = new string('b', DesktopMemo.Infrastructure.Memos.MemoMarkdownDocumentReader.MaxFrontMatterLength + 1);
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), $$"""
            ---
            title: "{{oversizedLine}}"
            createdAt: 2026-03-10T10:00:00+08:00
            updatedAt: 2026-03-10T10:05:00+08:00
            ---
            body
            """);

        var result = await service.SyncAsync();

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(1, result.ParseFailureCount);
    }

    [Fact]
    public async Task SyncAsync_CountsSkipped_WhenUpdateAffectsZeroRows()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-10T10:00:00+08:00");
        var updatedAt = DateTimeOffset.Parse("2026-03-10T10:05:00+08:00");

        await repository.AddAsync(CreateMemo(id, createdAt, updatedAt));
        await File.WriteAllTextAsync(GetMemoPath(tempDirectory.Path, id), BuildMarkdown(id, createdAt.AddDays(-2), updatedAt.AddDays(2)));
        await CreateIgnoreUpdateTriggerAsync(tempDirectory.Path, id);

        var result = await service.SyncAsync();
        var dbRow = await ReadDatabaseRecordAsync(tempDirectory.Path, id);

        Assert.Equal(1, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.NotNull(dbRow);
        Assert.Equal(createdAt.ToString("o"), dbRow!.Value.CreatedAt);
        Assert.Equal(updatedAt.ToString("o"), dbRow.Value.UpdatedAt);
    }

    [Fact]
    public async Task SyncAsync_IgnoresSoftDeletedMemos()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var repository = new SqliteIndexedMemoRepository(tempDirectory.Path);
        var logger = new TestLogService();
        var service = new MemoFrontMatterTimestampSyncService(tempDirectory.Path, logger);

        var id = Guid.NewGuid();
        await repository.AddAsync(CreateMemo(
            id,
            DateTimeOffset.Parse("2026-03-10T10:00:00+08:00"),
            DateTimeOffset.Parse("2026-03-10T10:05:00+08:00")));
        await repository.DeleteAsync(id);

        var result = await service.SyncAsync();

        Assert.Equal(0, result.ScannedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.ParseFailureCount);
    }

    private static Memo CreateMemo(Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        return new Memo(id, "标题", "body", "body", createdAt, updatedAt, Array.Empty<string>(), false);
    }

    private static string GetMemoPath(string dataDirectory, Guid id)
    {
        return Path.Combine(dataDirectory, "content", $"{id:N}.md");
    }

    private static string BuildMarkdown(Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        return $$"""
            ---
            id: {{id}}
            title: "标题"
            createdAt: {{createdAt:o}}
            updatedAt: {{updatedAt:o}}
            isPinned: False
            tags:
            ---
            body
            """;
    }

    private static async Task SetFilePathAsync(string dataDirectory, Guid id, string filePath)
    {
        var dbPath = Path.Combine(dataDirectory, "memos.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE memos SET file_path = $filePath WHERE id = $id";
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateIgnoreUpdateTriggerAsync(string dataDirectory, Guid id)
    {
        var dbPath = Path.Combine(dataDirectory, "memos.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = $$"""
            CREATE TRIGGER ignore_memo_update
            BEFORE UPDATE OF created_at, updated_at ON memos
            WHEN OLD.id = '{{id}}'
            BEGIN
                SELECT RAISE(IGNORE);
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(string CreatedAt, string UpdatedAt, string FilePath)?> ReadDatabaseRecordAsync(string dataDirectory, Guid id)
    {
        var dbPath = Path.Combine(dataDirectory, "memos.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at, updated_at, file_path FROM memos WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}

internal sealed class TestLogService : ILogService
{
    private readonly List<LogEntry> _logs = new();

    public event EventHandler<LogEntry>? LogAdded;

    public void Debug(string source, string message) => Add(new LogEntry(DateTimeOffset.Now, LogLevel.Debug, source, message));

    public void Info(string source, string message) => Add(new LogEntry(DateTimeOffset.Now, LogLevel.Info, source, message));

    public void Warning(string source, string message) => Add(new LogEntry(DateTimeOffset.Now, LogLevel.Warning, source, message));

    public void Error(string source, string message, Exception? exception = null) =>
        Add(new LogEntry(DateTimeOffset.Now, LogLevel.Error, source, message, exception));

    public void Log(LogEntry entry) => Add(entry);

    public IReadOnlyList<LogEntry> GetAllLogs() => _logs;

    public IReadOnlyList<LogEntry> GetLogsByLevel(LogLevel minLevel) => _logs.FindAll(log => log.Level >= minLevel);

    public void ClearLogs() => _logs.Clear();

    public Task<IReadOnlyList<LogEntry>> LoadHistoryLogsAsync(int maxCount = 1000) =>
        Task.FromResult<IReadOnlyList<LogEntry>>(_logs);

    private void Add(LogEntry entry)
    {
        _logs.Add(entry);
        LogAdded?.Invoke(this, entry);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DesktopMemoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(Path))
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    Task.Delay(50).GetAwaiter().GetResult();
                }
            }
        }
    }
}
