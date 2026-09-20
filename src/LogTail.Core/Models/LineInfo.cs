namespace LogTail.Core.Models;

/// <summary>
/// Lightweight value struct representing the disk location of a single log line.
/// Allows keeping millions of line pointers in RAM with near-zero memory footprint.
/// </summary>
public readonly record struct LineInfo(
    long StartOffset,
    int Length,
    long LineNumber);
