namespace LogTail.Core.Sources;

public interface ILogSource : IAsyncDisposable
{
    string DisplayName { get; }

    IObservable<Models.RawLogEvent> Events { get; }

    bool IsRunning { get; }

    Task StartAsync(CancellationToken ct);

    ValueTask StopAsync();
}
