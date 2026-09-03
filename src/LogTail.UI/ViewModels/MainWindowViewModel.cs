using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using LogTail.Core.Buffer;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using LogTail.Core.Pipeline;
using LogTail.Core.Sources;
using ReactiveUI;

namespace LogTail.UI.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private string _windowTitle = "Log Tail";

    public string WindowTitle
    {
        get => _windowTitle;
        set => this.RaiseAndSetIfChanged(ref _windowTitle, value);
    }

    private string? _currentFilePath;

    public string? CurrentFilePath
    {
        get => _currentFilePath;
        set => this.RaiseAndSetIfChanged(ref _currentFilePath, value);
    }

    private string _statusMessage = "No file open";

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    private bool _autoScroll = true;

    public bool AutoScroll
    {
        get => _autoScroll;
        set => this.RaiseAndSetIfChanged(ref _autoScroll, value);
    }

    private ThemeMode _currentTheme;

    public ThemeMode CurrentTheme
    {
        get => _currentTheme;
        set => this.RaiseAndSetIfChanged(ref _currentTheme, value);
    }

    public ObservableCollection<EnrichedLogEvent> VisibleEvents { get; } = new();

    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public ReactiveCommand<ThemeMode, Unit> SetThemeCommand { get; }

    private readonly ObservableAsPropertyHelper<bool> _isTailing;

    public bool IsTailing => _isTailing.Value;

    public bool CanOpenFile => !IsTailing;

    public Interaction<Unit, string?> ShowOpenFileDialog { get; } = new();

    private readonly SettingsStore _settings;
    private readonly ILogSourceFactory _sourceFactory;
    private readonly RingBuffer<EnrichedLogEvent> _buffer;
    private ILogSource? _source;

    public ILogSource? CurrentSource
    {
        get => _source;
        private set => this.RaiseAndSetIfChanged(ref _source, value);
    }

    private IDisposable? _eventsSubscription;

    public MainWindowViewModel(SettingsStore settings, ILogSourceFactory sourceFactory)
    {
        _settings = settings;
        _sourceFactory = sourceFactory;

        // Restore settings.
        var loaded = _settings.Load();
        CurrentTheme = loaded.Theme;
        _buffer = new RingBuffer<EnrichedLogEvent>(loaded.BufferCapacity > 0 ? loaded.BufferCapacity : 50_000);

        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        ClearCommand = ReactiveCommand.Create(Clear);
        SetThemeCommand = ReactiveCommand.Create<ThemeMode>(SetTheme);

        // Wire IsTailing from the currently active source's IsRunning state.
        _isTailing = this.WhenAnyValue(x => x.CurrentSource)
            .Select(source => source?.IsRunning == true)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.IsTailing);
    }

    private async Task OpenFileAsync()
    {
        // Request file path via ReactiveUI Interaction
        var selectedPath = await ShowOpenFileDialog.Handle(Unit.Default);

        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        // Close the previous source if one is open.
        if (CurrentSource != null)
        {
            await CurrentSource.StopAsync();
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;
            CurrentSource = null;
        }

        CurrentFilePath = selectedPath;

        var source = _sourceFactory.CreateFileSource(CurrentFilePath);
        CurrentSource = source;

        _eventsSubscription = source.Events
            .Select(raw => Enrich.Transform(raw))
            .Buffer(TimeSpan.FromMilliseconds(50), 200)
            .Where(batch => batch.Count > 0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                onNext: OnNewEventsBatch,
                onError: ex =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });

        await source.StartAsync(CancellationToken.None);

        StatusMessage = $"Tailing: {Path.GetFileName(CurrentFilePath)}";
    }

    private void OnNewEventsBatch(IList<EnrichedLogEvent> batch)
    {
        if (batch.Count == 0) return;

        int excess = (VisibleEvents.Count + batch.Count) - _buffer.Capacity;
        if (excess > 0)
        {
            if (excess >= VisibleEvents.Count)
            {
                VisibleEvents.Clear();
            }
            else
            {
                for (int i = 0; i < excess; i++)
                {
                    VisibleEvents.RemoveAt(0);
                }
            }
        }

        foreach (var item in batch)
        {
            _buffer.Add(item);
            VisibleEvents.Add(item);
        }
    }

    private void Clear()
    {
        _buffer.Clear();
        VisibleEvents.Clear();
    }

    private void SetTheme(ThemeMode mode)
    {
        CurrentTheme = mode;
        _settings.Update(s => s with { Theme = mode });
    }
}
