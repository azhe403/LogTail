namespace LogTail.Core.Sources;

public interface ILogSourceFactory
{
    ILogSource CreateFileSource(string filePath);
}
