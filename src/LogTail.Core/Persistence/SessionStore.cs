using System;
using System.IO;
using System.Text.Json;
using LogTail.Core.Logging;
using LogTail.Core.Models;

namespace LogTail.Core.Persistence;

public sealed class SessionStore
{
    private readonly string _sessionPath;
    private readonly ILogTailLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionStore(string appDataDirectory, ILogTailLogger? logger = null)
    {
        _sessionPath = Path.Combine(appDataDirectory, "session.json");
        _logger = logger ?? new ConsoleLogger();
    }

    public SessionState Load()
    {
        try
        {
            if (!File.Exists(_sessionPath))
            {
                return Empty;
            }

            var json = File.ReadAllText(_sessionPath);
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
            return state is null ? Empty : state;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load session, using empty: {ex.Message}");
            return Empty;
        }
    }

    public void Save(SessionState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(_sessionPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_sessionPath, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save session: {ex.Message}");
        }
    }

    public void Update(Func<SessionState, SessionState> mutate)
    {
        Save(mutate(Load()));
    }

    private static SessionState Empty => new(Array.Empty<string>(), null);
}
