using System.Reactive;
using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Sources;
using LogTail.UI.ViewModels;
using Xunit;

namespace LogTail.UI.Tests;

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
        sut.VisibleEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetThemeCommand_WhenExecuted_UpdatesCurrentTheme()
    {
        var sut = CreateViewModel();

        sut.SetThemeCommand.Execute(ThemeMode.Dark).Subscribe();

        sut.CurrentTheme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void ClearCommand_WhenExecuted_EmptiesVisibleEvents()
    {
        var sut = CreateViewModel();
        sut.VisibleEvents.Add(
            new EnrichedLogEvent(
                new RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "line"),
                LogLevel.Unknown,
                Timestamp: null,
                LevelColorKey: null));

        sut.ClearCommand.Execute().Subscribe();

        sut.VisibleEvents.Should().BeEmpty();
    }

    private MainWindowViewModel CreateViewModel()
    {
        var logger = new LogTail.Core.Logging.ConsoleLogger();
        var settings = new Core.Persistence.SettingsStore(_tempDir, logger);
        var factory = new LogSourceFactory(logger);

        return new MainWindowViewModel(settings, factory);
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
