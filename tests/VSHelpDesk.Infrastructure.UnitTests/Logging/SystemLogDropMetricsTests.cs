using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Infrastructure.Logging;

namespace VSHelpDesk.Infrastructure.UnitTests.Logging;

public sealed class SystemLogDropMetricsTests
{
    [Fact]
    public void DbLogger_WhenChannelFull_IncrementsDroppedCount()
    {
        var channel = Channel.CreateBounded<SystemLog>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var metrics = new SystemLogDropMetrics();
        var options = Options.Create(new DatabaseLoggingOptions());
        var provider = new DbLoggerProvider(channel.Writer, options, dropMetrics: metrics);
        var logger = provider.CreateLogger("Test");

        // Fill channel to capacity (1 item)
        logger.LogError("Message 1");
        Assert.Equal(0, metrics.DroppedCount);

        // TryWrite should fail now that channel is full (since FullMode = Wait)
        logger.LogError("Message 2");
        Assert.Equal(1, metrics.DroppedCount);
    }
}
