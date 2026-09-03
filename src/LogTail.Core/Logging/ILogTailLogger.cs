namespace LogTail.Core.Logging;

public interface ILogTailLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
    void Debug(string message);
}
