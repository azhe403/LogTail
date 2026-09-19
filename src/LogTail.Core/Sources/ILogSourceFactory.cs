namespace LogTail.Core.Sources;

public interface ILogSourceFactory
{
    ILogSource CreateFileSource(string filePath, int maxInitialLines = 50_000);
}
