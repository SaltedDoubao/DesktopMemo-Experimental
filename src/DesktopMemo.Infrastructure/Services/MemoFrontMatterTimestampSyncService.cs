using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DesktopMemo.Core.Contracts;
using DesktopMemo.Infrastructure.Memos;
using Microsoft.Data.Sqlite;

namespace DesktopMemo.Infrastructure.Services;

public sealed class MemoFrontMatterTimestampSyncService
{
    private readonly string _connectionString;
    private readonly ILogService _logService;

    public MemoFrontMatterTimestampSyncService(string dataDirectory, ILogService logService)
    {
        _connectionString = $"Data Source={Path.Combine(dataDirectory, "memos.db")}";
        _logService = logService;
    }

    public async Task<MemoFrontMatterTimestampSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetDatabasePath(out var dbPath) || !File.Exists(dbPath))
        {
            _logService.Debug("MemoTimestampSync", "未检测到 memos.db，跳过 Markdown 时间同步");
            return new MemoFrontMatterTimestampSyncResult(0, 0, 0, 0);
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string querySql = @"
            SELECT id, file_path AS FilePath, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM memos
            WHERE deleted_at IS NULL";

        var rows = await connection.QueryAsync<MemoTimestampRow>(querySql).ConfigureAwait(false);

        var scannedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;
        var parseFailureCount = 0;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedCount++;

            if (!File.Exists(row.FilePath))
            {
                skippedCount++;
                _logService.Warning("MemoTimestampSync", $"备忘录文件不存在，跳过时间同步: {row.Id}");
                continue;
            }

            try
            {
                var yaml = await MemoMarkdownDocumentReader.ReadFrontMatterAsync(row.FilePath, cancellationToken).ConfigureAwait(false);
                var frontMatter = MemoMarkdownFrontMatterParser.Parse(yaml);

                if (frontMatter.CreatedAt is null || frontMatter.UpdatedAt is null)
                {
                    skippedCount++;
                    parseFailureCount++;
                    _logService.Warning("MemoTimestampSync", $"备忘录 front matter 时间缺失或无效，跳过时间同步: {row.Id}");
                    continue;
                }

                var markdownCreatedAt = frontMatter.CreatedAt.Value;
                var markdownUpdatedAt = frontMatter.UpdatedAt.Value;
                var dbCreatedAt = ParseTimestamp(row.CreatedAt);
                var dbUpdatedAt = ParseTimestamp(row.UpdatedAt);

                if (dbCreatedAt == markdownCreatedAt && dbUpdatedAt == markdownUpdatedAt)
                {
                    continue;
                }

                const string updateSql = @"
                    UPDATE memos
                    SET created_at = @CreatedAt,
                        updated_at = @UpdatedAt
                    WHERE id = @Id AND deleted_at IS NULL";

                await connection.ExecuteAsync(updateSql, new
                {
                    row.Id,
                    CreatedAt = markdownCreatedAt.ToString("o", CultureInfo.InvariantCulture),
                    UpdatedAt = markdownUpdatedAt.ToString("o", CultureInfo.InvariantCulture)
                }).ConfigureAwait(false);

                updatedCount++;
            }
            catch (InvalidDataException ex)
            {
                skippedCount++;
                parseFailureCount++;
                _logService.Warning("MemoTimestampSync", $"备忘录 front matter 读取失败，跳过时间同步: {row.Id}，原因: {ex.Message}");
            }
            catch (IOException ex)
            {
                skippedCount++;
                _logService.Warning("MemoTimestampSync", $"备忘录文件读取失败，跳过时间同步: {row.Id}，原因: {ex.Message}");
            }
        }

        _logService.Info(
            "MemoTimestampSync",
            $"Markdown 时间同步完成: 扫描 {scannedCount}，更新 {updatedCount}，跳过 {skippedCount}，解析失败 {parseFailureCount}");

        return new MemoFrontMatterTimestampSyncResult(scannedCount, updatedCount, skippedCount, parseFailureCount);
    }

    private bool TryGetDatabasePath(out string? dbPath)
    {
        const string prefix = "Data Source=";
        if (_connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            dbPath = _connectionString[prefix.Length..];
            return true;
        }

        dbPath = null;
        return false;
    }

    private static DateTimeOffset? ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            return result;
        }

        return null;
    }

    private sealed class MemoTimestampRow
    {
        public string Id { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;
    }
}

public sealed record MemoFrontMatterTimestampSyncResult(
    int ScannedCount,
    int UpdatedCount,
    int SkippedCount,
    int ParseFailureCount);
