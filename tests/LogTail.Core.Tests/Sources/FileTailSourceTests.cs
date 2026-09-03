using System.Reactive.Linq;
using FluentAssertions;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Sources;
using Xunit;

namespace LogTail.Core.Tests.Sources;

public sealed class FileTailSourceTests : IAsyncLifetime
{
    private readonly string _tempDir;

    public FileTailSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        return Task.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // Polls `predicate` every 25ms up to `timeout`, returning true as soon as it holds.
    // Makes timing-sensitive integration tests deterministic under load.
    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }
            await Task.Delay(25);
        }
        return false;
    }

    [Fact]
    public async Task StartAsync_WhenNewLinesAppended_EmitsAppendedEvents()
    {
        var filePath = Path.Combine(_tempDir, "test.log");
        File.WriteAllText(filePath, "line1\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => true); // allow tail to settle

        await File.AppendAllTextAsync(filePath, "line2\nline3\n");

        var sawAll = await WaitUntilAsync(() =>
            events.Any(e => e.Line.Contains("line2")) && events.Any(e => e.Line.Contains("line3")));

        await sut.StopAsync();

        sawAll.Should().BeTrue("the two appended lines should both be emitted");
    }

    [Fact]
    public async Task StartAsync_WhenExistingFileTailed_EmitsExistingAndAppendedEvents()
    {
        var filePath = Path.Combine(_tempDir, "existing.log");
        File.WriteAllText(filePath, "old-line1\nold-line2\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        var sawOld = await WaitUntilAsync(() =>
            events.Any(e => e.Line == "old-line1") && events.Any(e => e.Line == "old-line2"));

        await File.AppendAllTextAsync(filePath, "new-line\n");

        var sawNew = await WaitUntilAsync(() => events.Any(e => e.Line == "new-line"));

        await sut.StopAsync();

        sawOld.Should().BeTrue("existing lines before start should be emitted");
        sawNew.Should().BeTrue("the newly appended line should also be emitted");
    }

    [Fact]
    public async Task StartAsync_WhenFileRotated_ReopensAndEmitsNewEvents()
    {
        var filePath = Path.Combine(_tempDir, "rotate.log");
        await File.WriteAllTextAsync(filePath, "before-rotate\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => true);

        // Simulate rotation: delete original, create new file with same name
        File.Delete(filePath);
        await File.AppendAllTextAsync(filePath, "after-rotate\n");

        var sawNew = await WaitUntilAsync(() => events.Any(e => e.Line == "after-rotate"));

        await sut.StopAsync();

        sawNew.Should().BeTrue("rotating to a new file with the same name should be detected and re-read");
    }

    [Fact]
    public async Task StopAsync_WhenInvoked_StopsEmittingEvents()
    {
        var filePath = Path.Combine(_tempDir, "stop.log");
        await File.WriteAllTextAsync(filePath, "start\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync();

        var countBefore = events.Count;

        await File.AppendAllTextAsync(filePath, "after-stop\n");
        await Task.Delay(300);

        events.Count.Should().Be(countBefore);
    }

    [Fact]
    public async Task DisplayName_WhenInitialized_ReturnsFileName()
    {
        var filePath = Path.Combine(_tempDir, "mylog.log");
        await File.WriteAllTextAsync(filePath, "");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        sut.DisplayName.Should().Be("mylog.log");
    }
}
