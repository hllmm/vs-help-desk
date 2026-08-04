using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace VSHelpDesk.Infrastructure.Logging;

/// <summary>
/// Options for production database logging infrastructure.
/// </summary>
public sealed class DatabaseLoggingOptions
{
    public const string SectionName = "DatabaseLogging";

    public LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    [Range(1, 1000, ErrorMessage = "BatchSize must be between 1 and 1000.")]
    public int BatchSize { get; set; } = 100;

    [Range(1, 365, ErrorMessage = "RetentionDays must be between 1 and 365.")]
    public int RetentionDays { get; set; } = 30;

    [Range(10, 50000, ErrorMessage = "QueueCapacity must be between 10 and 50000.")]
    public int QueueCapacity { get; set; } = 1000;

    public bool SanitizePII { get; set; } = true;
}
