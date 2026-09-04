using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using LogTail.Core.Logging;
using LogTail.Core.Models;

namespace LogTail.Core.Sources;

public sealed class FileTailSource : ILogSource
{
    private readonly string _filePath;
    private readonly TimeSpan _pollInterval;
    private readonly ILogTailLogger _logger;
    private readonly Subject<RawLogEvent> _events = new();
    private readonly object _readLock = new();

    private FileStream? _stream;
    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private CancellationTokenSource? _cts;
    private long _offset;
    private Task? _readLoop;
    private volatile bool _forceReopen;
    private readonly StringBuilder _partialLineBuffer = new();

    public FileTailSource(string filePath, TimeSpan pollInterval, ILogTailLogger logger)
    {
        _filePath = filePath;
        _pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(250) : pollInterval;
        _logger = logger;
    }

    public string DisplayName => Path.GetFileName(_filePath);

    public IObservable<RawLogEvent> Events => _events.AsObservable();

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Open file from end (tail mode)
        _stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        _offset = 0;

        // Set up FileSystemWatcher
        var dir = Path.GetDirectoryName(_filePath)!;
        var filter = Path.GetFileName(_filePath);

        _watcher = new FileSystemWatcher(dir, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        // Poll fallback
        _pollTimer = new Timer(PollCallback, null, _pollInterval, _pollInterval);

        // Read loop
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

        IsRunning = true;
        _logger.Debug($"Started tailing: {_filePath}");
    }

    public async ValueTask StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;

        _pollTimer?.Dispose();
        _pollTimer = null;

        _watcher?.Dispose();
        _watcher = null;

        _cts?.Cancel();

        if (_readLoop != null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _stream?.Dispose();
        _stream = null;

        _cts?.Dispose();
        _cts = null;

        _logger.Debug($"Stopped tailing: {_filePath}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _events.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        TriggerRead();
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.Warn($"File deleted: {e.FullPath}. Marking for reopen.");
        _forceReopen = true;
        TriggerRead();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        _logger.Warn($"File rotated: {e.OldFullPath} → {e.FullPath}");
        _forceReopen = true;
        TriggerRead();
    }

    private void PollCallback(object? state)
    {
        TriggerRead();
    }

    private void TriggerRead()
    {
        // Signal the read loop to attempt a read.
        // The read loop itself handles concurrency via Monitor.
        lock (_readLock)
        {
            Monitor.Pulse(_readLock);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                lock (_readLock)
                {
                    Monitor.Wait(_readLock, _pollInterval);
                }

                if (_forceReopen)
                {
                    _forceReopen = false;
                    await ReopenFileAsync().ConfigureAwait(false);
                    if (_stream == null)
                    {
                        continue;
                    }
                }
                else if (_stream == null || !File.Exists(_filePath))
                {
                    // File may have been deleted/rotated
                    await ReopenFileAsync().ConfigureAwait(false);
                    if (_stream == null)
                    {
                        continue;
                    }
                }

                var currentLength = new FileInfo(_filePath).Length;

                // Truncation detection: file shrunk below our offset
                if (currentLength < _offset)
                {
                    _logger.Warn($"File truncated (size {currentLength} < offset {_offset}). Resetting offset.");
                    _offset = 0;
                    _partialLineBuffer.Clear();
                }

                if (_stream.Position != _offset)
                {
                    _stream.Seek(_offset, SeekOrigin.Begin);
                }

                if (_offset >= currentLength)
                {
                    continue;
                }

                // Read available bytes
                var bufferSize = (int)Math.Min(currentLength - _offset, 64 * 1024);
                var buffer = new byte[bufferSize];

                int bytesRead = await _stream.ReadAsync(buffer, 0, bufferSize, ct).ConfigureAwait(false);

                if (bytesRead > 0)
                {
                    _offset += bytesRead;
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _partialLineBuffer.Append(text);

                    var combined = _partialLineBuffer.ToString();
                    var lines = combined.Split(["\r\n", "\n"], StringSplitOptions.None);

                    // The last item is either empty (if combined ended with a newline)
                    // or an incomplete partial line (which we retain in the buffer).
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i];
                        if (!string.IsNullOrEmpty(line))
                        {
                            _events.OnNext(new RawLogEvent(
                                ReadAt: DateTimeOffset.UtcNow,
                                SourceId: _filePath,
                                FileOffset: _offset,
                                Line: line));
                        }
                    }

                    _partialLineBuffer.Clear();
                    _partialLineBuffer.Append(lines[^1]);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Read error on {_filePath}", ex);
                await ReopenFileAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ReopenFileAsync()
    {
        _stream?.Dispose();
        _stream = null;

        // Wait for file to reappear
        for (int i = 0; i < 20; i++) // 2 seconds max
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    _stream = new FileStream(
                        _filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 4096,
                        FileOptions.Asynchronous);

                    _offset = 0; // Start from beginning of new file
                    _partialLineBuffer.Clear();
                    _logger.Info($"Reopened file: {_filePath} at offset 0");
                    return;
                }
                catch
                {
                    // File exists but locked, retry
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        _logger.Warn($"File not found after rotation: {_filePath}. Will keep retrying.");
    }
}
