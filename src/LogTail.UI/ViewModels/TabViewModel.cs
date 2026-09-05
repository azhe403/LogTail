using LogTail.Core.Models;
using LogTail.UI.Collections;
using ReactiveUI;

namespace LogTail.UI.ViewModels;

public sealed class TabViewModel : ReactiveObject
{
    private const int MaxLineLength = 4000;
    private string _fileName = string.Empty;
    private string _filePath = string.Empty;
    private string _status = "Idle";
    private bool _isLoading;
    private int _lineCount;
    private long _fileSize;
    private DateTime _lastModified;
    private double _linesPerSecond;
    private bool _isTailing;

    public string FileName
    {
        get => _fileName;
        set => this.RaiseAndSetIfChanged(ref _fileName, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            IsLoading = string.Equals(value, "loading", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public BulkObservableCollection<EnrichedLogEvent> LogEvents { get; } = new();

    public int LineCount
    {
        get => _lineCount;
        set => this.RaiseAndSetIfChanged(ref _lineCount, value);
    }

    public long FileSize
    {
        get => _fileSize;
        set => this.RaiseAndSetIfChanged(ref _fileSize, value);
    }

    public DateTime LastModified
    {
        get => _lastModified;
        set => this.RaiseAndSetIfChanged(ref _lastModified, value);
    }

    public double LinesPerSecond
    {
        get => _linesPerSecond;
        set => this.RaiseAndSetIfChanged(ref _linesPerSecond, value);
    }

    public bool IsTailing
    {
        get => _isTailing;
        set => this.RaiseAndSetIfChanged(ref _isTailing, value);
    }

    public TabViewModel(string filePath)
    {
        FilePath = filePath;
        FileName = System.IO.Path.GetFileName(filePath);

        // Best-effort: populate initial size and modified time so the status
        // bar shows something useful before tailing starts. Swallow IO errors
        // — validation already happened upstream; the tailing source will
        // surface any real problems when it actually opens the file.
        try
        {
            var info = new System.IO.FileInfo(filePath);
            if (info.Exists)
            {
                _fileSize = info.Length;
                _lastModified = info.LastWriteTime;
            }
        }
        catch (System.IO.IOException)
        {
            // Leave defaults (0 / default(DateTime)) — BuildTabStatus will
            // render these as "0 B" and empty time, not crash.
        }
        catch (System.UnauthorizedAccessException)
        {
            // Same: skip.
        }
    }

    public void AddLogEvent(EnrichedLogEvent logEvent)
    {
        var trimmed = TrimLineIfNeeded(logEvent);
        var numberedEvent = trimmed with { LineNumber = LineCount + 1 };
        LogEvents.Add(numberedEvent);
        LineCount = LogEvents.Count;
    }

    /// <summary>
    /// Append a batch with a single UI notification. Assigns line numbers
    /// sequentially and caps absurdly long lines so layout stays cheap.
    /// </summary>
    public void AddLogEvents(IList<EnrichedLogEvent> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0)
        {
            return;
        }

        var numbered = new List<EnrichedLogEvent>(batch.Count);
        var nextNumber = LineCount + 1;
        foreach (var item in batch)
        {
            numbered.Add(TrimLineIfNeeded(item) with { LineNumber = nextNumber });
            nextNumber++;
        }

        LogEvents.AddRange(numbered);
        LineCount = LogEvents.Count;
    }

    /// <summary>
    /// Evict count oldest items with a single UI notification.
    /// </summary>
    public void EvictFromFront(int count)
    {
        if (count <= 0)
        {
            return;
        }

        LogEvents.RemoveFromFront(count);
        LineCount = LogEvents.Count;
    }

    public void ClearEvents()
    {
        LogEvents.Clear();
        LineCount = 0;
    }

    private static EnrichedLogEvent TrimLineIfNeeded(EnrichedLogEvent logEvent)
    {
        var line = logEvent.Raw.Line;
        if (line.Length <= MaxLineLength)
        {
            return logEvent;
        }

        var trimmedRaw = logEvent.Raw with { Line = string.Concat(line.AsSpan(0, MaxLineLength), "…") };
        return logEvent with { Raw = trimmedRaw };
    }
}
