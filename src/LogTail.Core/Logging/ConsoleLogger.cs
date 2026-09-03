namespace LogTail.Core.Logging;

public sealed class ConsoleLogger : ILogTailLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO]  {message}");
    public void Warn(string message) => Console.WriteLine($"[WARN]  {message}");
    public void Error(string message, Exception? exception = null) =>
        Console.Error.WriteLine($"[ERROR] {message}{(exception != null ? $" | {exception}" : "")}");
    public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
}
