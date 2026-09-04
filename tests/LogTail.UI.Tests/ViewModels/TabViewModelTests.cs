using FluentAssertions;
using LogTail.Core.Models;
using LogTail.UI.ViewModels;
using Xunit;

namespace LogTail.UI.Tests.ViewModels;

public class TabViewModelTests
{
    [Fact]
    public void Constructor_SetsInitialValues()
    {
        var vm = new TabViewModel("/path/to/file.log");

        vm.FileName.Should().Be("file.log");
        vm.FilePath.Should().Be("/path/to/file.log");
        vm.Status.Should().Be("Idle");
        vm.IsTailing.Should().BeFalse();
        vm.LineCount.Should().Be(0);
    }

    [Fact]
    public void AddLogEvent_IncrementsLineCount()
    {
        var vm = new TabViewModel("/path/to/file.log");
        var raw = new RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "Test message");
        var logEvent = new EnrichedLogEvent(raw, LogLevel.Info, DateTimeOffset.Now, null);

        vm.AddLogEvent(logEvent);

        vm.LineCount.Should().Be(1);
        vm.LogEvents.Should().HaveCount(1);
    }

    [Fact]
    public void AddLogEvent_AssignsLineNumber()
    {
        var vm = new TabViewModel("/path/to/file.log");
        var raw1 = new RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "First line");
        var raw2 = new RawLogEvent(DateTimeOffset.UtcNow, "test", 10, "Second line");
        var event1 = new EnrichedLogEvent(raw1, LogLevel.Info, DateTimeOffset.Now, null);
        var event2 = new EnrichedLogEvent(raw2, LogLevel.Info, DateTimeOffset.Now, null);

        vm.AddLogEvent(event1);
        vm.AddLogEvent(event2);

        vm.LogEvents[0].LineNumber.Should().Be(1);
        vm.LogEvents[1].LineNumber.Should().Be(2);
    }

    [Fact]
    public void ClearEvents_ResetsCount()
    {
        var vm = new TabViewModel("/path/to/file.log");
        var raw = new RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "Test message");
        var logEvent = new EnrichedLogEvent(raw, LogLevel.Info, DateTimeOffset.Now, null);
        vm.AddLogEvent(logEvent);

        vm.ClearEvents();

        vm.LineCount.Should().Be(0);
        vm.LogEvents.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_PopulatesFileSizeAndModified_WhenFileExists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"logtail-tabtest-{Guid.NewGuid():N}.log");
        File.WriteAllText(tempFile, "INFO: line one\nWARN: line two\n");

        try
        {
            var vm = new TabViewModel(tempFile);

            vm.FileSize.Should().BeGreaterThan(0);
            vm.LastModified.Should().NotBe(default);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constructor_LeavesDefaults_WhenFileMissing()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.log");

        var vm = new TabViewModel(nonExistent);

        // Constructor must not throw for missing files — the upstream validator
        // already rejects those, but defensive code keeps a missing path from
        // crashing the tab.
        vm.FileSize.Should().Be(0);
        vm.LastModified.Should().Be(default);
        vm.IsTailing.Should().BeFalse();
    }
}
