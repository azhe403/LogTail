using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using Xunit;

namespace LogTail.Core.Tests.Persistence;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-test-{Guid.NewGuid():N}");
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
    public void Load_WhenFileDoesNotExist_ReturnsDefaultSettings()
    {
        var sut = new SettingsStore(_tempDir);

        var result = sut.Load();

        result.Should().Be(new AppSettings());
    }

    [Fact]
    public void Save_WhenSettingsSaved_RoundtripsSuccessfully()
    {
        var sut = new SettingsStore(_tempDir);
        var settings = new AppSettings(Theme: ThemeMode.Dark);

        sut.Save(settings);
        var loaded = sut.Load();

        loaded.Should().Be(settings);
    }

    [Fact]
    public void Update_WhenInvoked_ModifiesAndPersistsSettings()
    {
        var sut = new SettingsStore(_tempDir);
        sut.Save(new AppSettings());

        sut.Update(s => s with { Theme = ThemeMode.Light });
        var loaded = sut.Load();

        loaded.Theme.Should().Be(ThemeMode.Light);
    }

    [Fact]
    public void Load_WhenFileIsCorrupted_ReturnsDefaultSettings()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{{{{not json}}}}");

        var sut = new SettingsStore(_tempDir);

        var result = sut.Load();

        result.Should().Be(new AppSettings());
    }
}
