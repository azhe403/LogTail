using System.Buffers;
using System.Text;
using LogTail.Core.Models;

namespace LogTail.Core.Indexing;

public interface ILineReader
{
    Task<string> ReadLineAsync(string filePath, LineInfo lineInfo, Encoding? encoding = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ReadLinesAsync(string filePath, IReadOnlyList<LineInfo> lineInfos, Encoding? encoding = null, CancellationToken ct = default);
}

public sealed class FileLineReader : ILineReader
{
    public async Task<string> ReadLineAsync(
        string filePath,
        LineInfo lineInfo,
        Encoding? encoding = null,
        CancellationToken ct = default)
    {
        var targetEncoding = encoding ?? Encoding.UTF8;
        if (lineInfo.Length <= 0)
        {
            return string.Empty;
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        stream.Seek(lineInfo.StartOffset, SeekOrigin.Begin);

        byte[] rented = ArrayPool<byte>.Shared.Rent(lineInfo.Length);
        try
        {
            int read = await stream.ReadAsync(rented.AsMemory(0, lineInfo.Length), ct).ConfigureAwait(false);
            return targetEncoding.GetString(rented, 0, read);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async Task<IReadOnlyList<string>> ReadLinesAsync(
        string filePath,
        IReadOnlyList<LineInfo> lineInfos,
        Encoding? encoding = null,
        CancellationToken ct = default)
    {
        if (lineInfos.Count == 0)
        {
            return Array.Empty<string>();
        }

        var targetEncoding = encoding ?? Encoding.UTF8;
        var results = new List<string>(lineInfos.Count);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous);

        foreach (var info in lineInfos)
        {
            if (info.Length <= 0)
            {
                results.Add(string.Empty);
                continue;
            }

            stream.Seek(info.StartOffset, SeekOrigin.Begin);
            byte[] rented = ArrayPool<byte>.Shared.Rent(info.Length);
            try
            {
                int read = await stream.ReadAsync(rented.AsMemory(0, info.Length), ct).ConfigureAwait(false);
                results.Add(targetEncoding.GetString(rented, 0, read));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return results;
    }
}
