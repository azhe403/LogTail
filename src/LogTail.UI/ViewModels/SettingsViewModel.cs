using System;
using System.Collections.Generic;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using ReactiveUI;

namespace LogTail.UI.ViewModels;

public sealed class SettingsViewModel : ReactiveObject
{
    private readonly SettingsStore _settings;
    private ThemeMode _theme;
    private bool _restoreLastSession;

    public SettingsViewModel(SettingsStore settings, ILogTailLogger? logger = null)
    {
        _settings = settings;
        var loaded = _settings.Load();
        _theme = loaded.Theme;
        _restoreLastSession = loaded.RestoreLastSession;
    }

    public ThemeMode Theme
    {
        get => _theme;
        set
        {
            if (_theme == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _theme, value);
            _settings.Update(s => s with { Theme = value });
            ThemeChanged?.Invoke(value);
        }
    }

    public bool RestoreLastSession
    {
        get => _restoreLastSession;
        set
        {
            if (_restoreLastSession == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _restoreLastSession, value);
            _settings.Update(s => s with { RestoreLastSession = value });
            RestoreLastSessionChanged?.Invoke(value);
        }
    }

    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = new[]
    {
        ThemeMode.System,
        ThemeMode.Light,
        ThemeMode.Dark
    };

    public event Action<ThemeMode>? ThemeChanged;

    public event Action<bool>? RestoreLastSessionChanged;
}
