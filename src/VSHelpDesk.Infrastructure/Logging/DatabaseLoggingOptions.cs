
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace VSHelpDesk.Infrastructure.Logging;

public sealed class DatabaseLoggingOptions
{
    public const string SectionName = "DatabaseLogging";
    public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    [Range(1, 1000)] public int BatchSize { get; set; } = 100;
    [Range(1, 365)] public int RetentionDays { get; set; } = 30;
    [Range(10, 50000)] public int QueueCapacity { get; set; } = 1000;
    [Range(1, 10)] public int MaxWriteAttempts { get; set; } = 3;
    [Range(50, 10000)] public int RetryBaseDelayMilliseconds { get; set; } = 500;
    [Range(100, 5000)] public int RetentionBatchSize { get; set; } = 1000;
    public bool SanitizePII { get; set; } = true;
}
