using System.Collections.ObjectModel;
using LogTail.Core.Models;
using ReactiveUI;

namespace LogTail.UI.ViewModels;

public sealed class TabViewModel : ReactiveObject
{
    private string _fileName = string.Empty;
    private string _filePath = string.Empty;
    private string _status = "Idle";
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
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public ObservableCollection<EnrichedLogEvent> LogEvents { get; } = new();

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
        var numberedEvent = logEvent with { LineNumber = LineCount + 1 };
        LogEvents.Add(numberedEvent);
        LineCount = LogEvents.Count;
    }

    public void ClearEvents()
    {
        LogEvents.Clear();
        LineCount = 0;
    }
}
