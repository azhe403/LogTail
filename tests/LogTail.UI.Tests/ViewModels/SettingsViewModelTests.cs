using System;
using System.IO;
using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using LogTail.UI.ViewModels;
using Xunit;

namespace LogTail.UI.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-settings-vm-test-{Guid.NewGuid():N}");
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
    public void Constructor_ReadsStoredTheme()
    {
        var store = new SettingsStore(_tempDir);
        store.Save(new AppSettings(Theme: ThemeMode.Dark));

        var sut = new SettingsViewModel(store);

        sut.Theme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void Theme_WhenSet_PersistsAndRaisesEvent()
    {
        var store = new SettingsStore(_tempDir);
        var sut = new SettingsViewModel(store);
        ThemeMode? observed = null;
        sut.ThemeChanged += mode => observed = mode;

        sut.Theme = ThemeMode.Light;

        observed.Should().Be(ThemeMode.Light);
        store.Load().Theme.Should().Be(ThemeMode.Light);
    }

    [Fact]
    public void RestoreLastSession_WhenSet_PersistsAndRaisesEvent()
    {
        var store = new SettingsStore(_tempDir);
        var sut = new SettingsViewModel(store);
        bool? observed = null;
        sut.RestoreLastSessionChanged += enabled => observed = enabled;

        sut.RestoreLastSession = false;

        observed.Should().BeFalse();
        store.Load().RestoreLastSession.Should().BeFalse();
    }
}
