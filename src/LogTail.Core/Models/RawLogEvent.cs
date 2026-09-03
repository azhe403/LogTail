namespace LogTail.Core.Models;

public readonly record struct RawLogEvent(
    DateTimeOffset ReadAt,
    string SourceId,
    long FileOffset,
    string Line,
    bool IsHistorical = false);
