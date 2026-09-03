using LogTail.Core.Models;

namespace LogTail.Core.Pipeline;

/// <summary>
/// Milestone 1: no-op pipeline stage.
/// Always returns EnrichedLogEvent with Level=Unknown, Timestamp=null,
/// IsHighlighted=false, IsHidden=false.
/// M2 will add actual level detection, timestamp parsing, filter, and highlight.
/// </summary>
public static class Enrich
{
    public static EnrichedLogEvent Transform(RawLogEvent raw)
    {
        return new EnrichedLogEvent(
            Raw: raw,
            Level: LogLevel.Unknown,
            Timestamp: null,
            LevelColorKey: null,
            IsHighlighted: false,
            IsHidden: false);
    }
}
