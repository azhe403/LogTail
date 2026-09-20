using FluentAssertions;
using LogTail.Core.Indexing;
using Xunit;

namespace LogTail.Core.Tests.Indexing;

public sealed class FileIndexerAndReaderTests : IDisposable
{
    private readonly string _tempDir;

    public FileIndexerAndReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-indexer-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task IndexAndRead_WithMixedNewlines_ReturnsExactLines()
    {
        var filePath = Path.Combine(_tempDir, "mixed-newlines.log");
        var content = "Line 1: Hello\r\nLine 2: World\nLine 3: TailBlazer\r\nLine 4: Without trailing newline";
        await File.WriteAllTextAsync(filePath, content);

        var indexer = new FileIndexer();
        var reader = new FileLineReader();

        var indexes = await indexer.IndexFileAsync(filePath);

        indexes.Should().HaveCount(4);
        indexes[0].LineNumber.Should().Be(1);
        indexes[1].LineNumber.Should().Be(2);
        indexes[2].LineNumber.Should().Be(3);
        indexes[3].LineNumber.Should().Be(4);

        var line1 = await reader.ReadLineAsync(filePath, indexes[0]);
        var line2 = await reader.ReadLineAsync(filePath, indexes[1]);
        var line3 = await reader.ReadLineAsync(filePath, indexes[2]);
        var line4 = await reader.ReadLineAsync(filePath, indexes[3]);

        line1.Should().Be("Line 1: Hello");
        line2.Should().Be("Line 2: World");
        line3.Should().Be("Line 3: TailBlazer");
        line4.Should().Be("Line 4: Without trailing newline");
    }

    [Fact]
    public async Task ReadLinesAsync_InBatch_ReturnsAllLinesCorrectly()
    {
        var filePath = Path.Combine(_tempDir, "batch-read.log");
        var expectedLines = Enumerable.Range(1, 1000).Select(i => $"Log entry #{i:D4} - test payload").ToList();
        await File.WriteAllLinesAsync(filePath, expectedLines);

        var indexer = new FileIndexer();
        var reader = new FileLineReader();

        var indexes = await indexer.IndexFileAsync(filePath);
        indexes.Should().HaveCount(1000);

        // Read a window (e.g. visible slice in UI viewport: lines 500 to 550)
        var sliceIndices = indexes.Skip(500).Take(50).ToList();
        var sliceLines = await reader.ReadLinesAsync(filePath, sliceIndices);

        sliceLines.Should().HaveCount(50);
        sliceLines[0].Should().Be(expectedLines[500]);
        sliceLines[^1].Should().Be(expectedLines[549]);
    }
}
