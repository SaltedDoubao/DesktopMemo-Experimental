using System;
using System.Collections.Generic;
using System.Threading;

namespace DesktopMemo.Core.Models;

/// <summary>
/// 日志条目
/// </summary>
public sealed record LogEntry
{
    /// <summary>
    /// 日志时间
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
    
    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel Level { get; init; }
    
    /// <summary>
    /// 日志来源（模块名称）
    /// </summary>
    public string Source { get; init; }
    
    /// <summary>
    /// 日志消息
    /// </summary>
    public string Message { get; init; }
    
    /// <summary>
    /// 异常信息（可选）
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 进程 ID
    /// </summary>
    public int ProcessId { get; init; } = Environment.ProcessId;

    /// <summary>
    /// 线程 ID
    /// </summary>
    public int ThreadId { get; init; } = Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    /// 线程名称（可选）
    /// </summary>
    public string ThreadName { get; init; } = Thread.CurrentThread.Name ?? string.Empty;

    /// <summary>
    /// 会话 ID（可选）
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// 应用版本（可选）
    /// </summary>
    public string AppVersion { get; init; } = string.Empty;

    public LogEntry(DateTimeOffset timestamp, LogLevel level, string source, string message, Exception? exception = null)
    {
        Timestamp = timestamp;
        Level = level;
        Source = source ?? string.Empty;
        Message = message ?? string.Empty;
        Exception = exception;
    }

    /// <summary>
    /// 获取日志级别的显示名称
    /// </summary>
    public string LevelName => Level switch
    {
        LogLevel.Debug => "调试",
        LogLevel.Info => "信息",
        LogLevel.Warning => "警告",
        LogLevel.Error => "错误",
        _ => "未知"
    };

    /// <summary>
    /// 获取完整的日志文本（用于文件输出）
    /// </summary>
    public string ToLogString()
    {
        var exceptionInfo = Exception != null ? $"\n{Exception}" : string.Empty;
        var contextParts = new List<string>
        {
            $"pid={ProcessId}",
            $"tid={ThreadId}"
        };

        if (!string.IsNullOrWhiteSpace(ThreadName))
        {
            contextParts.Add($"thread={ThreadName}");
        }

        if (!string.IsNullOrWhiteSpace(AppVersion))
        {
            contextParts.Add($"ver={AppVersion}");
        }

        if (!string.IsNullOrWhiteSpace(SessionId))
        {
            contextParts.Add($"sid={SessionId}");
        }

        var context = contextParts.Count > 0
            ? $" [{string.Join(" ", contextParts)}]"
            : string.Empty;

        return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] [{Source}]{context} {Message}{exceptionInfo}";
    }
}

