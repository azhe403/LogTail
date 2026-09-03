namespace LogTail.Core.Models;

public sealed record AppSettings(
    ThemeMode Theme = ThemeMode.System,
    int BufferCapacity = 50_000,
    TimeSpan PollInterval = default,
    string DefaultEncoding = "utf-8");
