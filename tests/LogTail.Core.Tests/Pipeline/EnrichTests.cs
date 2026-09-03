using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Pipeline;
using Xunit;

namespace LogTail.Core.Tests.Pipeline;

public sealed class EnrichTests
{
    [Fact]
    public void Transform_WhenRawEventProvided_SetsLevelToUnknown()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "2026-09-02 INFO Application started");

        var result = Enrich.Transform(raw);

        result.Level.Should().Be(LogLevel.Unknown);
    }

    [Fact]
    public void Transform_WhenRawEventProvided_SetsTimestampToNull()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "2026-09-02 INFO Application started");

        var result = Enrich.Transform(raw);

        result.Timestamp.Should().BeNull();
    }

    [Fact]
    public void Transform_WhenRawEventProvided_SetsIsHighlightedToFalse()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "some line");

        var result = Enrich.Transform(raw);

        result.IsHighlighted.Should().BeFalse();
    }

    [Fact]
    public void Transform_WhenRawEventProvided_SetsIsHiddenToFalse()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "some line");

        var result = Enrich.Transform(raw);

        result.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Transform_WhenRawEventProvided_PreservesRawEvent()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 100,
            Line: "hello world");

        var result = Enrich.Transform(raw);

        result.Raw.Should().Be(raw);
    }
}
