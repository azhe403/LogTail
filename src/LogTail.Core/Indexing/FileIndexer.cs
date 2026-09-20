using System.Buffers;
using LogTail.Core.Models;

namespace LogTail.Core.Indexing;

public interface IFileIndexer
{
    Task<IReadOnlyList<LineInfo>> IndexFileAsync(
        string filePath,
        long startByteOffset = 0,
        long startingLineNumber = 1,
        CancellationToken ct = default);
}

public sealed class FileIndexer : IFileIndexer
{
    private const int BufferSize = 64 * 1024;

    public async Task<IReadOnlyList<LineInfo>> IndexFileAsync(
        string filePath,
        long startByteOffset = 0,
        long startingLineNumber = 1,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return Array.Empty<LineInfo>();
        }

        var lines = new List<LineInfo>();
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: BufferSize,
            FileOptions.Asynchronous);

        if (startByteOffset > 0 && startByteOffset < stream.Length)
        {
            stream.Seek(startByteOffset, SeekOrigin.Begin);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long currentLineStart = stream.Position;
        long currentLineNumber = startingLineNumber;
        long globalOffset = stream.Position;

        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
            {
                int bufferIndex = 0;
                while (bufferIndex < bytesRead)
                {
                    byte b = buffer[bufferIndex];
                    if (b == (byte)'\n')
                    {
                        long lineEnd = globalOffset + bufferIndex;
                        int length = (int)(lineEnd - currentLineStart);

                        // Strip trailing \r if present
                        if (length > 0 && bufferIndex > 0 && buffer[bufferIndex - 1] == (byte)'\r')
                        {
                            length--;
                        }
                        else if (length > 0 && bufferIndex == 0 && currentLineStart < lineEnd)
                        {
                            // If \r was at the very end of previous buffer chunk, adjust length
                            // (Handled naturally because length is relative to currentLineStart)
                        }

                        lines.Add(new LineInfo(
                            StartOffset: currentLineStart,
                            Length: Math.Max(0, length),
                            LineNumber: currentLineNumber++));

                        currentLineStart = lineEnd + 1; // skip \n
                    }

                    bufferIndex++;
                }

                globalOffset += bytesRead;
            }

            // If there is trailing content without a trailing newline
            if (currentLineStart < globalOffset)
            {
                int trailingLength = (int)(globalOffset - currentLineStart);
                lines.Add(new LineInfo(
                    StartOffset: currentLineStart,
                    Length: trailingLength,
                    LineNumber: currentLineNumber));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return lines;
    }
}
