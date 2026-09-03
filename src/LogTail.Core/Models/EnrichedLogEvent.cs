namespace LogTail.Core.Models;

public sealed record EnrichedLogEvent(
    RawLogEvent Raw,
    LogLevel Level,
    DateTimeOffset? Timestamp,
    string? LevelColorKey,
    bool IsHighlighted = false,
    bool IsHidden = false);
