using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
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

    private TabViewModel? _selectedTab;
    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    private readonly ObservableAsPropertyHelper<string> _selectedTabStatus;
    public string SelectedTabStatus => _selectedTabStatus.Value;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

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

    private int _bufferCapacity;
    public int BufferCapacity
    {
        get => _bufferCapacity;
        private set => this.RaiseAndSetIfChanged(ref _bufferCapacity, value);
    }

    private IDisposable? _eventsSubscription;
    private readonly Dictionary<TabViewModel, IDisposable> _rateSubscriptions = new();

    public MainWindowViewModel(SettingsStore settings, ILogSourceFactory sourceFactory)
    {
        _settings = settings;
        _sourceFactory = sourceFactory;

        // Restore settings.
        var loaded = _settings.Load();
        CurrentTheme = loaded.Theme;
        _buffer = new RingBuffer<EnrichedLogEvent>(loaded.BufferCapacity > 0 ? loaded.BufferCapacity : 50_000);
        BufferCapacity = _buffer.Capacity;

        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        ClearCommand = ReactiveCommand.Create(Clear);
        SetThemeCommand = ReactiveCommand.Create<ThemeMode>(SetTheme);

        // Wire IsTailing from the currently active source's IsRunning state.
        _isTailing = this.WhenAnyValue(x => x.CurrentSource)
            .Select(source => source?.IsRunning == true)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, x => x.IsTailing);

        // Compose the status bar summary for the active tab (size, modified time,
        // lines-per-second, tailing state). Recomputes when any of those change.
        // BufferCapacity is global (one shared RingBuffer), so the trigger
        // re-fires when it changes too — though M1 only sets it once at ctor.
        _selectedTabStatus = this.WhenAnyValue(
                x => x.SelectedTab,
                x => x.BufferCapacity)
            .Select(tuple => tuple.Item1 is null
                ? Observable.Return(string.Empty)
                : tuple.Item1.WhenAnyValue(
                        t => t.IsTailing,
                        t => t.FileSize,
                        t => t.LastModified,
                        t => t.LinesPerSecond,
                        t => t.LogEvents.Count)
                    .Select(_ => BuildTabStatus(tuple.Item1, tuple.Item2)))
            .Switch()
            .ToProperty(this, x => x.SelectedTabStatus);

        // Drive tailing from the selected tab: switching tabs stops the old source
        // and starts tailing the newly-selected file into that tab's LogEvents.
        this.WhenAnyValue(x => x.SelectedTab)
            .Skip(1)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async tab =>
            {
                CurrentFilePath = tab?.FilePath;

                if (tab == null)
                {
                    await StopCurrentSourceAsync();
                    StatusMessage = "No file open";
                    return;
                }

                StatusMessage = $"Tailing: {Path.GetFileName(tab.FilePath)}";
                try
                {
                    await StartTailingAsync(tab);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                }
            });
    }

    private async Task OpenFileAsync()
    {
        var selectedPath = await ShowOpenFileDialog.Handle(Unit.Default);
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        await OpenFileAndAddTabAsync(selectedPath);
    }

    /// <summary>
    /// Open a file by creating (or reusing) a tab and starting its tail source.
    /// Used by the File→Open flow; dropped-file path goes through <see cref="AddTab"/>.
    /// </summary>
    internal async Task OpenFileAndAddTabAsync(string filePath)
    {
        var existing = FindTabByPath(filePath);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new TabViewModel(filePath);
        AttachLinesPerSecondCounter(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
        // Tail will start via SelectedTab observer.
        await Task.CompletedTask;
    }

    public void AddTab(string filePath)
    {
        var existingTab = Tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return;
        }

        var tab = new TabViewModel(filePath);
        AttachLinesPerSecondCounter(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
        // Tail starts via WhenAnyValue(SelectedTab) subscription above.
    }

    public void CloseTab(TabViewModel tab)
    {
        var closingActive = SelectedTab == tab;

        // Dispose the rate-counter subscription for this tab so we don't leak
        // when the user closes many tabs over the session.
        if (_rateSubscriptions.Remove(tab, out var rateSub))
        {
            rateSub.Dispose();
        }

        Tabs.Remove(tab);

        if (closingActive)
        {
            // Stop the running source if the closed tab was the active one.
            Task.Run(StopCurrentSourceAsync);
            SelectedTab = Tabs.FirstOrDefault();
        }
    }

    /// <summary>
    /// Wires a 1-second rolling rate counter to <paramref name="tab"/>. Counts
    /// CollectionChanged notifications from <c>tab.LogEvents</c> per second and
    /// pushes the value to <c>tab.LinesPerSecond</c>. Subscription is disposed
    /// by <see cref="CloseTab"/>.
    /// </summary>
    private void AttachLinesPerSecondCounter(TabViewModel tab)
    {
        var subscription = Observable.FromEventPattern<
                NotifyCollectionChangedEventHandler,
                NotifyCollectionChangedEventArgs>(
                handler => tab.LogEvents.CollectionChanged += handler,
                handler => tab.LogEvents.CollectionChanged -= handler)
            .Buffer(TimeSpan.FromSeconds(1))
            .Select(events => (double)events.Count)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(rate => tab.LinesPerSecond = rate);

        _rateSubscriptions[tab] = subscription;
    }

    public TabViewModel? FindTabByPath(string filePath)
    {
        return Tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task StopCurrentSourceAsync()
    {
        if (CurrentSource != null)
        {
            await CurrentSource.StopAsync();
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;
            CurrentSource = null;
        }

        // Mark the previously active tab as no longer tailing. Null-check
        // SelectedTab because this can run during teardown.
        var stopped = SelectedTab;
        if (stopped is not null)
        {
            stopped.IsTailing = false;
        }
    }

    private async Task StartTailingAsync(TabViewModel tab)
    {
        await StopCurrentSourceAsync();

        // Refresh size & modified time in case the file changed between tab
        // creation and tail start (e.g. dropped file is appended to right
        // after drop). Best-effort; ignore IO errors.
        try
        {
            var info = new FileInfo(tab.FilePath);
            if (info.Exists)
            {
                tab.FileSize = info.Length;
                tab.LastModified = info.LastWriteTime;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        tab.IsTailing = true;
        tab.Status = "tailing";

        var source = _sourceFactory.CreateFileSource(tab.FilePath);
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
                    tab.IsTailing = false;
                    tab.Status = "error";
                    StatusMessage = $"Error: {ex.Message}";
                });

        await source.StartAsync(CancellationToken.None);
    }

    private void OnNewEventsBatch(IList<EnrichedLogEvent> batch)
    {
        if (batch.Count == 0) return;

        var tab = SelectedTab;
        if (tab == null) return;

        int excess = (tab.LogEvents.Count + batch.Count) - _buffer.Capacity;
        if (excess > 0)
        {
            if (excess >= tab.LogEvents.Count)
            {
                tab.LogEvents.Clear();
            }
            else
            {
                for (int i = 0; i < excess; i++)
                {
                    tab.LogEvents.RemoveAt(0);
                }
            }
        }

        foreach (var item in batch)
        {
            _buffer.Add(item);
            tab.AddLogEvent(item);
        }
    }

    private void Clear()
    {
        _buffer.Clear();
        if (SelectedTab is not null)
        {
            SelectedTab.ClearEvents();
        }
    }

    private static string BuildTabStatus(TabViewModel tab, int bufferCapacity)
    {
        var size = tab.FileSize;
        var sizeText = size switch
        {
            >= 1024L * 1024 * 1024 => $"{size / (1024.0 * 1024 * 1024):0.##} GB",
            >= 1024L * 1024 => $"{size / (1024.0 * 1024):0.##} MB",
            >= 1024 => $"{size / 1024.0:0.##} KB",
            _ => $"{size} B",
        };

        // Use em-dash placeholder when modified time is unavailable (file stat
        // failed or missing). Avoids empty string in the status bar which
        // produces a double "•" separator and looks broken.
        var modified = tab.LastModified == default
            ? "—"
            : tab.LastModified.ToString("HH:mm:ss");

        // Always show rate — including 0 when idle — so the status bar reads
        // consistently (no missing field that flickers in/out as events arrive).
        var rate = $"{tab.LinesPerSecond:0.#} lines/s";

        var state = tab.IsTailing ? "tailing" : tab.Status;

        // Format count with thousands separator (e.g. 5,234) to match size/date
        // readability.
        var countText = $"{tab.LogEvents.Count:N0} / {bufferCapacity:N0} lines";

        return $"{tab.FilePath} | {sizeText} | {modified} | {countText} | {rate} | {state}";
    }

    private void SetTheme(ThemeMode mode)
    {
        CurrentTheme = mode;
        _settings.Update(s => s with { Theme = mode });
    }
}
