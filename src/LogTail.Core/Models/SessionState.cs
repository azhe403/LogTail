using System.Collections.Generic;

namespace LogTail.Core.Models;

public sealed record SessionState(
    IReadOnlyList<string> Files,
    string? ActiveFile);
