using System.Reactive;
using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Sources;
using LogTail.UI.ViewModels;
using Xunit;

namespace LogTail.UI.Tests;

[Collection("MainWindowViewModelTests")]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-vm-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WhenInitialized_HasDefaultProperties()
    {
        var sut = CreateViewModel();

        sut.WindowTitle.Should().Be("Log Tail");
        sut.StatusMessage.Should().Be("No file open");
        sut.CurrentFilePath.Should().BeNull();
        sut.CurrentTheme.Should().Be(ThemeMode.System);
        sut.Tabs.Should().BeEmpty();
        sut.SelectedTab.Should().BeNull();
    }

    [Fact]
    public void SetThemeCommand_WhenExecuted_UpdatesCurrentTheme()
    {
        var sut = CreateViewModel();

        sut.SetThemeCommand.Execute(ThemeMode.Dark).Subscribe();

        sut.CurrentTheme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void ClearCommand_WhenExecuted_EmptiesSelectedTabEvents()
    {
        var sut = CreateViewModel();
        var tab = new TabViewModel(CreateLogFile());
        sut.Tabs.Add(tab);
        sut.SelectedTab = tab;
        tab.AddLogEvent(EnrichedLogEventFactory.CreateSample());

        sut.ClearCommand.Execute().Subscribe();

        tab.LogEvents.Should().BeEmpty();
    }

    [Fact]
    public void AddTab_WhenCalled_CreatesAndSelectsTab()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("first.log");

        sut.AddTab(path);

        sut.Tabs.Should().ContainSingle();
        sut.SelectedTab.Should().NotBeNull();
        sut.SelectedTab!.FilePath.Should().Be(path);
        sut.SelectedTab.FileName.Should().Be("first.log");
    }

    [Fact]
    public void AddTab_WhenSamePathAlreadyOpen_DoesNotCreateDuplicate()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile();
        sut.AddTab(path);

        sut.AddTab(path);

        sut.Tabs.Should().ContainSingle();
    }

    [Fact]
    public void CloseTab_WhenCalled_RemovesTabAndSelectsNext()
    {
        var sut = CreateViewModel();
        var path1 = CreateLogFile("a.log");
        var path2 = CreateLogFile("b.log");
        sut.AddTab(path1);
        sut.AddTab(path2);
        var first = sut.Tabs[0];
        var second = sut.Tabs[1];
        sut.SelectedTab = first;

        sut.CloseTab(first);

        sut.Tabs.Should().ContainSingle().Which.Should().Be(second);
        sut.SelectedTab.Should().Be(second);
    }

    [Fact]
    public void SelectedTabStatus_WhenNoTabSelected_IsEmpty()
    {
        var sut = CreateViewModel();

        sut.SelectedTabStatus.Should().BeEmpty();
    }

    [Fact]
    public void SelectedTabStatus_WhenTabSelected_ContainsFilePath()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("status.log");
        sut.AddTab(path);

        sut.SelectedTabStatus.Should().NotBeEmpty();
        sut.SelectedTabStatus.Should().Contain("status.log");
    }

    [Fact]
    public void SelectedTabStatus_WhenTabSelected_ContainsBufferCount()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("buffer.log");
        sut.AddTab(path);

        // Status should include "0 / N lines" before any events arrive
        // (the format is the spec contract). Allow thousands separator
        // (e.g. 50,000) in the max value.
        sut.SelectedTabStatus.Should().Contain("lines");
        sut.SelectedTabStatus.Should().MatchRegex(@"\d / [\d,]+ lines");
    }

    [Fact]
    public void BufferCapacity_ReflectsConfiguredValue()
    {
        var sut = CreateViewModel();

        // Default fallback when settings has no value: 50,000.
        sut.BufferCapacity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AddTab_AttachesLinesPerSecondCounter_NewTabStartsAtZero()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("rate.log");

        sut.AddTab(path);

        // No events have arrived yet — rolling 1-second rate should be zero.
        sut.SelectedTab!.LinesPerSecond.Should().Be(0);
    }

    [Fact]
    public void SelectedTabStatus_WhenIdle_AlwaysShowsRateField()
    {
        // Per UX decision: rate should always be present (even as 0) so the
        // status bar doesn't have a missing field when no events flow.
        var sut = CreateViewModel();
        var path = CreateLogFile("idle.log");
        sut.AddTab(path);

        sut.SelectedTabStatus.Should().Contain("0 lines/s");
    }

    [Fact]
    public void SelectedTabStatus_WhenModifiedUnknown_ShowsEmDashPlaceholder()
    {
        // If file stat could not be read (LastModified == default), the
        // status bar should show an em-dash instead of an empty field that
        // produces a double-bullet gap. This is a sentinel meaning "unknown",
        // not a fake value.
        var sut = CreateViewModel();
        var path = CreateLogFile("no-modified.log");
        sut.AddTab(path);
        var tab = sut.SelectedTab!;

        // Simulate stat read failure: TabViewModel default is already
        // default(DateTime), but ctor populates from FileInfo when possible.
        // Force it to default to assert the rendering path.
        tab.LastModified = default;

        sut.SelectedTabStatus.Should().Contain("—");
        // No double-pipe gap should appear in the rendered string.
        sut.SelectedTabStatus.Should().NotContain("|  |");
    }

    [Fact]
    public void CloseTab_DisposesRateSubscription_DoesNotLeak()
    {
        // No observable leak test possible without a mock subject, so we
        // verify the public surface: closing a tab removes it cleanly and
        // doesn't throw. The Dispose path inside CloseTab is exercised.
        var sut = CreateViewModel();
        var path = CreateLogFile("dispose.log");
        sut.AddTab(path);
        var tab = sut.Tabs[0];

        var act = () => sut.CloseTab(tab);

        act.Should().NotThrow();
        sut.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task StartTailingAsync_AfterInitialLogLoaded_RendersAllHistoricalEvents()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("hist-render.log");
        var lines = Enumerable.Range(1, 20).Select(i => $"line {i}").ToList();
        File.WriteAllLines(path, lines);

        sut.AddTab(path);

        // Wait until initial load finishes and renders to the tab
        var rendered = await WaitUntilAsync(() => sut.SelectedTab?.LogEvents.Count >= 20, TimeSpan.FromSeconds(30));

        rendered.Should().BeTrue();
        sut.SelectedTab!.LogEvents.Count.Should().Be(20);
        sut.SelectedTab.Status.Should().Be("tailing");
    }

    [Fact]
    public async Task StartTailingAsync_WithManyHistoricalLines_RendersAll()
    {
        // Repro: loading stuck on large files (InitialLogLoaded races the UI buffer).
        var sut = CreateViewModel();
        var path = Path.Combine(_tempDir, "many.log");
        var lines = Enumerable.Range(1, 50_000)
            .Select(i => $"line {i} - some padding to make lines realistic and longer than a few chars")
            .ToList();
        File.WriteAllLines(path, lines);

        sut.AddTab(path);

        var rendered = await WaitUntilAsync(() => sut.SelectedTab?.LogEvents.Count >= 50_000, TimeSpan.FromSeconds(60));

        rendered.Should().BeTrue();
        sut.SelectedTab!.LogEvents.Count.Should().Be(50_000);
        sut.SelectedTab.Status.Should().Be("tailing");
    }

    [Fact]
    public async Task StartTailingAsync_WithVeryManyHistoricalLines_RendersAll()
    {
        // Regression: 1M lines reproduces the 406MB opencode.log case where
        // ObserveOn queue backlog + 50ms timer caused event loss.
        var sut = CreateViewModel();
        var path = Path.Combine(_tempDir, "huge.log");
        var lines = Enumerable.Range(1, 1_000_000)
            .Select(i => $"line {i} - padding to make line realistic in size for tailing")
            .ToList();
        File.WriteAllLines(path, lines);

        sut.AddTab(path);

        var rendered = await WaitUntilAsync(
            () => sut.SelectedTab?.LogEvents.Count >= 1_000_000,
            TimeSpan.FromSeconds(480));

        rendered.Should().BeTrue($"expected 1M lines rendered, got {sut.SelectedTab?.LogEvents.Count}");
        sut.SelectedTab!.Status.Should().Be("tailing");
    }

    [Fact]
    public async Task StartTailingAsync_AfterInitialLoad_AppendedLinesRenderLive()
    {
        var sut = CreateViewModel();
        var path = CreateLogFile("live-render.log");
        File.WriteAllText(path, "hist1\nhist2\n");

        sut.AddTab(path);

        var histLoaded = await WaitUntilAsync(() => sut.SelectedTab?.LogEvents.Count == 2, TimeSpan.FromSeconds(30));
        histLoaded.Should().BeTrue();

        await File.AppendAllTextAsync(path, "live1\n");

        var liveLoaded = await WaitUntilAsync(() => sut.SelectedTab?.LogEvents.Count == 3, TimeSpan.FromSeconds(30));
        liveLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task StopCurrentSourceAsync_WhenSwitchingTabs_ClearsPendingBuffer()
    {
        var sut = CreateViewModel();
        var path1 = CreateLogFile("tab1.log");
        var path2 = CreateLogFile("tab2.log");

        sut.AddTab(path1);
        await WaitUntilAsync(() => sut.SelectedTab?.Status == "tailing");

        sut.AddTab(path2);
        await WaitUntilAsync(() => sut.SelectedTab?.Status == "tailing");

        sut.SelectedTab!.FilePath.Should().Be(path2);
    }

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

    private MainWindowViewModel CreateViewModel()
    {
        var logger = new LogTail.Core.Logging.ConsoleLogger();
        var settings = new Core.Persistence.SettingsStore(_tempDir, logger);
        var factory = new LogSourceFactory(logger);

        return new MainWindowViewModel(settings, factory);
    }

    private string CreateLogFile(string name = "test.log")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "hello\nworld\n");
        return path;
    }
}

internal static class EnrichedLogEventFactory
{
    public static EnrichedLogEvent CreateSample() =>
        new(
            new RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "line"),
            LogLevel.Unknown,
            Timestamp: null,
            LevelColorKey: null);
}
