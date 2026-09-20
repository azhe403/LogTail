using LogTail.Core.Logging;

namespace LogTail.Core.Sources;

public sealed class LogSourceFactory : ILogSourceFactory
{
    private readonly ILogTailLogger _logger;
    private readonly TimeSpan _pollInterval;

    public LogSourceFactory(ILogTailLogger logger, TimeSpan pollInterval = default)
    {
        _logger = logger;
        _pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(250) : pollInterval;
    }

    public ILogSource CreateFileSource(
        string filePath,
        int tailLineLimit = 50_000,
        int initialWindowBytes = 8 * 1024 * 1024,
        int maxWindowBytes = 64 * 1024 * 1024)
    {
        return new FileTailSource(
            filePath,
            _pollInterval,
            _logger,
            tailLineLimit,
            initialWindowBytes,
            maxWindowBytes);
    }
}
