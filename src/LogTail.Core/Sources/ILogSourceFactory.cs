namespace LogTail.Core.Sources;

public interface ILogSourceFactory
{
    ILogSource CreateFileSource(
        string filePath,
        int tailLineLimit = 50_000,
        int initialWindowBytes = 8 * 1024 * 1024,
        int maxWindowBytes = 64 * 1024 * 1024);
}
