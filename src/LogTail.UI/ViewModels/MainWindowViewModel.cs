using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Threading;
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
    private Action? _onInitialLoadedHandler;
    private readonly List<EnrichedLogEvent> _pendingHistoricalEvents = new();
    private readonly object _historicalLock = new();
    private volatile bool _historicalFlushed;
    private readonly Dictionary<TabViewModel, IDisposable> _rateSubscriptions = new();
    private readonly Dictionary<TabViewModel, Queue<DateTimeOffset>> _rateWindows = new();
    private readonly IDisposable _rateTimer;

    public MainWindowViewModel(SettingsStore settings, ILogSourceFactory sourceFactory)
    {
        _settings = settings;
        _sourceFactory = sourceFactory;

        // Restore settings.
        var loaded = _settings.Load();
        CurrentTheme = loaded.Theme;
        var initial = loaded.BufferCapacity > 0 ? loaded.BufferCapacity : 50_000;
        var max = loaded.MaxBufferCapacity >= initial ? loaded.MaxBufferCapacity : initial;
        _buffer = new RingBuffer<EnrichedLogEvent>(initial, max);
        _buffer.Grew += (_, newCapacity) =>
            Dispatcher.UIThread.Post(() => BufferCapacity = newCapacity);
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

        // Decay rate to zero when idle: prune 1-second windows every second.
        _rateTimer = Observable.Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => PruneRates());

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

                if (tab.LogEvents.Count > 0)
                {
                    StatusMessage = $"Tailing: {Path.GetFileName(tab.FilePath)}";
                    try
                    {
                        await StartTailingAsync(tab, resumeOnly: true);
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error: {ex.Message}";
                    }
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
    /// Tracks a 1-second rolling rate per tab based on added lines (not
    /// CollectionChanged events, so eviction never inflates the rate and
    /// bulk Reset notifications stay correct). Cleanup happens in
    /// <see cref="CloseTab"/>.
    /// </summary>
    private void AttachLinesPerSecondCounter(TabViewModel tab)
    {
        if (!_rateWindows.ContainsKey(tab))
        {
            _rateWindows[tab] = new Queue<DateTimeOffset>();
        }

        _rateSubscriptions[tab] = Disposable.Create(() => _rateWindows.Remove(tab));
    }

    private void RecordLinesForRate(TabViewModel tab, int addedCount)
    {
        if (addedCount <= 0)
        {
            return;
        }

        if (!_rateWindows.TryGetValue(tab, out var window))
        {
            window = new Queue<DateTimeOffset>();
            _rateWindows[tab] = window;
        }

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < addedCount; i++)
        {
            window.Enqueue(now);
        }

        PruneWindow(window, now);
        tab.LinesPerSecond = window.Count;
    }

    private void PruneRates()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (tab, window) in _rateWindows.ToArray())
        {
            var before = window.Count;
            PruneWindow(window, now);
            if (window.Count != before)
            {
                tab.LinesPerSecond = window.Count;
            }
        }
    }

    private static void PruneWindow(Queue<DateTimeOffset> window, DateTimeOffset now)
    {
        while (window.Count > 0 && (now - window.Peek()) > TimeSpan.FromSeconds(1))
        {
            window.Dequeue();
        }
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
            if (_onInitialLoadedHandler != null)
            {
                CurrentSource.InitialLogLoaded -= _onInitialLoadedHandler;
                _onInitialLoadedHandler = null;
            }

            await CurrentSource.StopAsync();
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;
            CurrentSource = null;
        }

        lock (_historicalLock)
        {
            _pendingHistoricalEvents.Clear();
            _historicalFlushed = false;
        }

        // Mark the previously active tab as no longer tailing. Null-check
        // SelectedTab because this can run during teardown.
        var stopped = SelectedTab;
        if (stopped is not null)
        {
            stopped.IsTailing = false;
        }
    }

    private async Task StartTailingAsync(TabViewModel tab, bool resumeOnly = false)
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
        tab.Status = resumeOnly ? "tailing" : "loading";

        var source = _sourceFactory.CreateFileSource(tab.FilePath);
        CurrentSource = source;

        if (resumeOnly)
        {
            // Tab already has its historical snapshot — skip re-reading the
            // whole file. Start the source from the existing end of file so
            // only new appends come in as live events.
            var resumeOffset = new FileInfo(tab.FilePath).Length;
            source.SeekToOffset(resumeOffset);
        }

        _historicalFlushed = false;

        _onInitialLoadedHandler = () =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                OnInitialLogLoaded();
            }
            else
            {
                Dispatcher.UIThread.Post(() => OnInitialLogLoaded());
            }
        };
        source.InitialLogLoaded += _onInitialLoadedHandler;

        _eventsSubscription = source.Events
            .Select(raw => (Raw: raw, Enriched: Enrich.Transform(raw)))
            .Buffer(TimeSpan.FromMilliseconds(50), 200)
            .Where(batch => batch.Count > 0)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                onNext: batch =>
                {
                    if (resumeOnly)
                    {
                        OnNewEventsBatch(batch.Select(b => b.Enriched).ToList());
                        return;
                    }

                    bool isHistoricalBatch = batch[0].Raw.IsHistorical;
                    if (isHistoricalBatch)
                    {
                        lock (_historicalLock)
                        {
                            foreach (var (_, enriched) in batch)
                            {
                                _pendingHistoricalEvents.Add(enriched);
                            }
                        }
                    }
                    else
                    {
                        if (!_historicalFlushed)
                        {
                            List<EnrichedLogEvent> snapshot;
                            lock (_historicalLock)
                            {
                                snapshot = new List<EnrichedLogEvent>(_pendingHistoricalEvents);
                            }
                            if (snapshot.Count > 0)
                            {
                                FlushPendingHistorical(snapshot);
                            }
                        }
                        OnNewEventsBatch(batch.Select(b => b.Enriched).ToList());
                    }
                },
                onError: ex =>
                {
                    tab.IsTailing = false;
                    tab.Status = "error";
                    StatusMessage = $"Error: {ex.Message}";
                });

        await source.StartAsync(CancellationToken.None);
    }

    private void FlushPendingHistorical(List<EnrichedLogEvent> snapshot)
    {
        var tab = SelectedTab;
        if (tab is null)
        {
            return;
        }

        tab.Status = "tailing";

        var excess = (tab.LogEvents.Count + snapshot.Count) - _buffer.Capacity;
        if (excess > 0)
        {
            tab.EvictFromFront(excess);
        }

        foreach (var item in snapshot)
        {
            _buffer.Add(item);
        }

        tab.AddLogEvents(snapshot);
        RecordLinesForRate(tab, snapshot.Count);

        lock (_historicalLock)
        {
            _pendingHistoricalEvents.Clear();
            _historicalFlushed = true;
        }
    }

    private void OnInitialLogLoaded()
    {
        var tab = SelectedTab;

        List<EnrichedLogEvent> snapshot;
        lock (_historicalLock)
        {
            if (_historicalFlushed)
            {
                if (tab != null)
                {
                    tab.Status = "tailing";
                }
                return;
            }
            snapshot = new List<EnrichedLogEvent>(_pendingHistoricalEvents);
        }

        if (tab is null)
        {
            lock (_historicalLock)
            {
                _historicalFlushed = true;
            }
            return;
        }

        FlushPendingHistorical(snapshot);
    }

    private void OnNewEventsBatch(IList<EnrichedLogEvent> batch)
    {
        if (batch.Count == 0) return;

        var tab = SelectedTab;
        if (tab == null) return;

        // Bulk evict + bulk append: 2 UI notifications per batch instead of
        // ~2*batch.Count. Avoids O(n*m) RemoveAt(0) loop at 50k capacity.
        var excess = (tab.LogEvents.Count + batch.Count) - _buffer.Capacity;
        if (excess > 0)
        {
            tab.EvictFromFront(excess);
        }

        foreach (var item in batch)
        {
            _buffer.Add(item);
        }

        tab.AddLogEvents(batch);
        RecordLinesForRate(tab, batch.Count);
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
