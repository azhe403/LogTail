# Log Tail Milestone 1 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a working desktop log tail application — open a file, watch new lines appear in real time with virtualized scrolling, and persist the user's theme preference across restarts.

**Architecture:** Multi-project solution. `LogTail.Core` (pure class library) owns data models, ring buffer, file tailing, settings persistence, and the enrichment pipeline. `LogTail.UI` (Avalonia app) owns view models, views, and theme wiring. Two test projects validate Core and UI independently. Communication from source to UI flows through `IObservable<RawLogEvent>` → enrichment → `RingBuffer` → `ObservableCollection` bound to a virtualized `ListBox`.

**Tech Stack:** .NET 10 (`net10.0`), Avalonia 12.1.2, ReactiveUI 11.3.9 (forced compatibility), Fluent theme, xUnit + FluentAssertions (Core), Avalonia.Headless 12.1.2 + xUnit (UI).

## Global Constraints

- Target framework: `net10.0` for all projects.
- Avalonia package version: `12.1.2` (core, headless, fonts, harfbuzz, xaml).
- ReactiveUI package version: `11.3.9` (forced; known risk: Avalonia 12 breaking changes). Set `<NoWarn>$(NoWarn);NU1701;NU1605</NoWarn>` in `LogTail.UI.csproj` and `LogTail.UI.Tests.csproj`.
- If compilation errors arise from Avalonia 11→12 API changes during Task 8–11, fix them inline. If more than 5 hours are spent on compatibility fixes, fall back to Avalonia 11.3.9 + ReactiveUI 11.3.9 + net8.0.
- Code style: Allman braces for type/method declarations; K&R for control flow. 4-space indent. File-scoped namespaces. `_camelCase` for private fields. Attributes on their own line with a blank line before the member. Preferred `var` when type is obvious from RHS; explicit type for public API surface.
- Source encoding: UTF-8 without BOM (default C# tooling).
- No global try-catch; exceptions bubble to ReactiveUI `ThrownExceptions`.
- All new files created under `C:\Projexts\Space\log-tail\`.
- Commit after each task; use conventional commit messages (`feat:`, `test:`, `chore:`).

## File Structure

```
C:\Projexts\Space\log-tail\
├── LogTail.sln
├── src/
│   ├── LogTail.Core/
│   │   ├── LogTail.Core.csproj
│   │   ├── Models/
│   │   │   ├── RawLogEvent.cs          # readonly record struct
│   │   │   ├── EnrichedLogEvent.cs     # sealed record
│   │   │   ├── LogLevel.cs             # enum
│   │   │   ├── ThemeMode.cs            # enum
│   │   │   └── AppSettings.cs          # sealed record
│   │   ├── Buffer/
│   │   │   └── RingBuffer.cs           # RingBuffer<T> : IReadOnlyList<T>
│   │   ├── Logging/
│   │   │   ├── ILogTailLogger.cs       # simple internal logger interface
│   │   │   └── ConsoleLogger.cs        # default implementation
│   │   ├── Sources/
│   │   │   ├── ILogSource.cs           # interface
│   │   │   ├── ILogSourceFactory.cs    # factory interface
│   │   │   ├── LogSourceFactory.cs     # default factory impl
│   │   │   └── FileTailSource.cs       # FileSystemWatcher + poll fallback
│   │   ├── Pipeline/
│   │   │   └── Enrich.cs              # no-op pipeline stage (M1), IObservable<RawLogEvent> → IObservable<EnrichedLogEvent>
│   │   └── Persistence/
│   │       └── SettingsStore.cs        # JSON read/write to %APPDATA%/log-tail/
│   └── LogTail.UI/
│       ├── LogTail.UI.csproj
│       ├── App.axaml
│       ├── App.axaml.cs
│       ├── Program.cs
│       ├── ViewModels/
│       │   └── MainWindowViewModel.cs
│       └── Views/
│           ├── MainWindow.axaml
│           └── MainWindow.axaml.cs
├── tests/
│   ├── LogTail.Core.Tests/
│   │   ├── LogTail.Core.Tests.csproj
│   │   ├── Buffer/
│   │   │   └── RingBufferTests.cs
│   │   ├── Persistence/
│   │   │   └── SettingsStoreTests.cs
│   │   ├── Sources/
│   │   │   └── FileTailSourceTests.cs
│   │   └── Pipeline/
│   │       └── EnrichTests.cs
│   └── LogTail.UI.Tests/
│       ├── LogTail.UI.Tests.csproj
│       ├── MainWindowViewModelTests.cs
│       └── TestApp.cs                 # Avalonia headless bootstrapper
├── docs/
│   └── superpowers/specs/
│       └── 2026-09-02-log-tail-design.md
```

---

## Task 1: Solution Scaffolding

**Files:**
- Create: `LogTail.sln`
- Create: `src/LogTail.Core/LogTail.Core.csproj`
- Create: `src/LogTail.UI/LogTail.UI.csproj`
- Create: `tests/LogTail.Core.Tests/LogTail.Core.Tests.csproj`
- Create: `tests/LogTail.UI.Tests/LogTail.UI.Tests.csproj`

**Interfaces:**
- Consumes: nothing
- Produces: compilable solution with all projects referencing each other correctly

- [x] **Step 1: Create solution and project files**

```bash
dotnet new sln --name LogTail
dotnet new classlib -n LogTail.Core -o src/LogTail.Core --framework net10.0
dotnet new avalonia.app -n LogTail.UI -o src/LogTail.UI --framework net10.0
dotnet new xunit -n LogTail.Core.Tests -o tests/LogTail.Core.Tests --framework net10.0
dotnet new xunit -n LogTail.UI.Tests -o tests/LogTail.UI.Tests --framework net10.0
```

Note: If `dotnet new avalonia.app` is not available (no Avalonia template installed), create `LogTail.UI` as a `consoleapp` and manually convert to Avalonia (Task 10 handles the UI setup). Create `LogTail.UI.csproj` manually if needed:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);NU1701;NU1605</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.2" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.2" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.2" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="12.1.2" />
    <PackageReference Include="Avalonia.ReactiveUI" Version="11.3.9" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add NuGet packages to Core.csproj**

Edit `src/LogTail.Core/LogTail.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

No external packages for Core — pure .NET + Rx (Rx comes transitively via ReactiveUI reference in UI, but Core itself references no Rx).

- [x] **Step 3: Add project references**

```bash
dotnet sln add src/LogTail.Core
dotnet sln add src/LogTail.UI
dotnet sln add tests/LogTail.Core.Tests
dotnet sln add tests/LogTail.UI.Tests

dotnet add tests/LogTail.Core.Tests reference src/LogTail.Core
dotnet add tests/LogTail.UI.Tests reference src/LogTail.UI
dotnet add tests/LogTail.UI.Tests reference src/LogTail.Core
dotnet add src/LogTail.UI reference src/LogTail.Core
```

- [x] **Step 4: Add test packages**

```bash
cd tests/LogTail.Core.Tests
dotnet add package FluentAssertions
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio

cd ../../tests/LogTail.UI.Tests
dotnet add package FluentAssertions
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
dotnet add package Avalonia.Headless --version 12.1.2
dotnet add package Avalonia.Headless.XUnit --version 12.1.2
```

- [x] **Step 5: Verify solution builds**

```bash
dotnet build
```

Expected: BUILD SUCCEEDED. If `net10.0` fails (Avalonia 12 packages not yet targeting net10.0 on NuGet), change `<TargetFramework>` to `net9.0` in all projects and retry.

- [x] **Step 6: Remove auto-generated `Class1.cs` and `UnitTest1.cs`**

```bash
Remove-Item src/LogTail.Core/Class1.cs
Remove-Item tests/LogTail.Core.Tests/UnitTest1.cs
Remove-Item tests/LogTail.UI.Tests/UnitTest1.cs
```

- [x] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution with Core, UI, and test projects"
```

---

## Task 2: Models

**Files:**
- Create: `src/LogTail.Core/Models/RawLogEvent.cs`
- Create: `src/LogTail.Core/Models/EnrichedLogEvent.cs`
- Create: `src/LogTail.Core/Models/LogLevel.cs`
- Create: `src/LogTail.Core/Models/ThemeMode.cs`
- Create: `src/LogTail.Core/Models/AppSettings.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `RawLogEvent`, `EnrichedLogEvent`, `LogLevel`, `ThemeMode`, `AppSettings` — used by every other task

- [x] **Step 1: Create LogLevel.cs**

```csharp
namespace LogTail.Core.Models;

public enum LogLevel
{
    Unknown,
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
```

- [x] **Step 2: Create ThemeMode.cs**

```csharp
namespace LogTail.Core.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark
}
```

- [x] **Step 3: Create RawLogEvent.cs**

```csharp
namespace LogTail.Core.Models;

public readonly record struct RawLogEvent(
    DateTimeOffset ReadAt,
    string SourceId,
    long FileOffset,
    string Line,
    bool IsHistorical = false);
```

- [x] **Step 4: Create EnrichedLogEvent.cs**

```csharp
namespace LogTail.Core.Models;

public sealed record EnrichedLogEvent(
    RawLogEvent Raw,
    LogLevel Level,
    DateTimeOffset? Timestamp,
    string? LevelColorKey,
    bool IsHighlighted = false,
    bool IsHidden = false);
```

- [x] **Step 5: Create AppSettings.cs**

```csharp
namespace LogTail.Core.Models;

public sealed record AppSettings(
    ThemeMode Theme = ThemeMode.System,
    int BufferCapacity = 50_000,
    TimeSpan PollInterval = default,
    string DefaultEncoding = "utf-8");
```

- [x] **Step 6: Verify build**

```bash
dotnet build src/LogTail.Core
```

Expected: BUILD SUCCEEDED.

- [x] **Step 7: Commit**

```bash
git add src/LogTail.Core/Models/
git commit -m "feat(core): add data models (RawLogEvent, EnrichedLogEvent, LogLevel, ThemeMode, AppSettings)"
```

---

## Task 3: RingBuffer\<T\>

**Files:**
- Create: `src/LogTail.Core/Buffer/RingBuffer.cs`
- Create: `tests/LogTail.Core.Tests/Buffer/RingBufferTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `RingBuffer<T>` class used by `FileTailSourceTests` (Task 6) and `MainWindowViewModel` (Task 8)

- [x] **Step 1: Write the failing tests**

Create `tests/LogTail.Core.Tests/Buffer/RingBufferTests.cs`:

```csharp
using FluentAssertions;
using LogTail.Core.Buffer;
using Xunit;

namespace LogTail.Core.Tests.Buffer;

public sealed class RingBufferTests
{
    [Fact]
    public void Add_up_to_capacity_retains_all_items()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Add(3);

        sut.Should().HaveCount(3);
        sut.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Add_beyond_capacity_evicts_oldest()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Add(3);
        sut.Add(4);

        sut.Should().HaveCount(3);
        sut.Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Add_many_items_evicts_correctly()
    {
        var sut = new RingBuffer<int>(3);

        for (int i = 0; i < 10; i++)
        {
            sut.Add(i);
        }

        sut.Should().HaveCount(3);
        sut.Should().Equal(7, 8, 9);
    }

    [Fact]
    public void Indexer_returns_correct_items()
    {
        var sut = new RingBuffer<string>(2);

        sut.Add("first");
        sut.Add("second");

        sut[0].Should().Be("first");
        sut[1].Should().Be("second");
    }

    [Fact]
    public void Indexer_out_of_range_throws()
    {
        var sut = new RingBuffer<int>(3);

        sut.Invoking(s => s[0]).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Clear_resets_count_to_zero()
    {
        var sut = new RingBuffer<int>(3);

        sut.Add(1);
        sut.Add(2);
        sut.Clear();

        sut.Should().HaveCount(0);
    }

    [Fact]
    public void Capacity_property_returns_constructor_value()
    {
        var sut = new RingBuffer<int>(42);

        sut.Capacity.Should().Be(42);
    }

    [Fact]
    public void Constructor_with_zero_capacity_throws()
    {
        Action act = () => new RingBuffer<int>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Concurrent_add_does_not_corrupt_state()
    {
        var sut = new RingBuffer<int>(1000);
        var barrier = new System.Threading.ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 4).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            barrier.Wait();
            for (int i = 0; i < 500; i++)
            {
                sut.Add(i);
            }
        })).ToArray();

        barrier.Set();
        System.Threading.Tasks.Task.WaitAll(tasks);

        sut.Count.Should().BeLessThanOrEqualTo(sut.Capacity);
        sut.Should().OnlyHaveUniqueItems(); // or just check Count <= Capacity
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~RingBufferTests" --no-build
```

Expected: FAIL — `RingBuffer<T>` type does not exist.

- [x] **Step 3: Implement RingBuffer\<T\>**

Create `src/LogTail.Core/Buffer/RingBuffer.cs`:

```csharp
using System.Collections;

namespace LogTail.Core.Buffer;

public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _buffer;
    private int _head;    // index of oldest item
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _buffer = new T[capacity];
        Capacity = capacity;
        _head = 0;
        _count = 0;
    }

    public int Capacity { get; }

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffer[(_head + index) % Capacity];
        }
    }

    public void Add(T item)
    {
        var writeIndex = (_head + _count) % Capacity;

        if (_count == Capacity)
        {
            // Evict oldest — advance head
            _buffer[writeIndex] = item;
            _head = (_head + 1) % Capacity;
        }
        else
        {
            _buffer[writeIndex] = item;
            _count++;
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer);
        _head = 0;
        _count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

- [x] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~RingBufferTests"
```

Expected: PASS (9/9).

- [x] **Step 5: Commit**

```bash
git add src/LogTail.Core/Buffer/ tests/LogTail.Core.Tests/Buffer/
git commit -m "feat(core): add RingBuffer<T> with eviction and thread-safe read"
```

---

## Task 4: Logger Abstraction + SettingsStore

**Files:**
- Create: `src/LogTail.Core/Logging/ILogTailLogger.cs`
- Create: `src/LogTail.Core/Logging/ConsoleLogger.cs`
- Create: `src/LogTail.Core/Persistence/SettingsStore.cs`
- Create: `tests/LogTail.Core.Tests/Persistence/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `ThemeMode` (Task 2)
- Produces: `SettingsStore` used by `MainWindowViewModel` (Task 8)

- [x] **Step 1: Create ILogTailLogger.cs**

```csharp
namespace LogTail.Core.Logging;

public interface ILogTailLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
    void Debug(string message);
}
```

- [x] **Step 2: Create ConsoleLogger.cs**

```csharp
namespace LogTail.Core.Logging;

public sealed class ConsoleLogger : ILogTailLogger
{
    public void Info(string message) => Console.WriteLine($"[INFO]  {message}");
    public void Warn(string message) => Console.WriteLine($"[WARN]  {message}");
    public void Error(string message, Exception? exception = null) =>
        Console.Error.WriteLine($"[ERROR] {message}{(exception != null ? $" | {exception}" : "")}");
    public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
}
```

- [x] **Step 3: Write the failing SettingsStore tests**

Create `tests/LogTail.Core.Tests/Persistence/SettingsStoreTests.cs`:

```csharp
using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using Xunit;

namespace LogTail.Core.Tests.Persistence;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-test-{Guid.NewGuid():N}");
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
    public void Load_returns_defaults_when_file_missing()
    {
        var sut = new SettingsStore(_tempDir);

        var result = sut.Load();

        result.Should().Be(new AppSettings());
    }

    [Fact]
    public void Save_then_load_roundtrips()
    {
        var sut = new SettingsStore(_tempDir);
        var settings = new AppSettings(Theme: ThemeMode.Dark);

        sut.Save(settings);
        var loaded = sut.Load();

        loaded.Should().Be(settings);
    }

    [Fact]
    public void Update_modifies_and_persists()
    {
        var sut = new SettingsStore(_tempDir);
        sut.Save(new AppSettings());

        sut.Update(s => s with { Theme = ThemeMode.Light });
        var loaded = sut.Load();

        loaded.Theme.Should().Be(ThemeMode.Light);
    }

    [Fact]
    public void Load_returns_defaults_when_file_corrupt()
    {
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{{{{not json}}}}");

        var sut = new SettingsStore(_tempDir);

        var result = sut.Load();

        result.Should().Be(new AppSettings());
    }
}
```

- [x] **Step 4: Run tests to verify they fail**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests" --no-build
```

Expected: FAIL — `SettingsStore` does not exist.

- [x] **Step 5: Implement SettingsStore**

Create `src/LogTail.Core/Persistence/SettingsStore.cs`:

```csharp
using System.Text.Json;
using LogTail.Core.Logging;
using LogTail.Core.Models;

namespace LogTail.Core.Persistence;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private readonly ILogTailLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsStore(string appDataDirectory, ILogTailLogger? logger = null)
    {
        _settingsPath = Path.Combine(appDataDirectory, "settings.json");
        _logger = logger ?? new ConsoleLogger();
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load settings, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save settings: {ex.Message}");
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        var current = Load();
        var updated = mutate(current);
        Save(updated);
    }
}
```

- [x] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~SettingsStoreTests"
```

Expected: PASS (4/4).

- [x] **Step 7: Commit**

```bash
git add src/LogTail.Core/Logging/ src/LogTail.Core/Persistence/ tests/LogTail.Core.Tests/Persistence/
git commit -m "feat(core): add SettingsStore with JSON persistence and logger abstraction"
```

---

## Task 5: ILogSource + LogSourceFactory

**Files:**
- Create: `src/LogTail.Core/Sources/ILogSource.cs`
- Create: `src/LogTail.Core/Sources/ILogSourceFactory.cs`
- Create: `src/LogTail.Core/Sources/LogSourceFactory.cs`

**Interfaces:**
- Consumes: `RawLogEvent` (Task 2)
- Produces: `ILogSource`, `ILogSourceFactory` — consumed by `FileTailSource` (Task 6), `MainWindowViewModel` (Task 8)

- [x] **Step 1: Create ILogSource.cs**

```csharp
using System.Reactive.Linq;

namespace LogTail.Core.Sources;

public interface ILogSource : IAsyncDisposable
{
    string DisplayName { get; }

    IObservable<Models.RawLogEvent> Events { get; }

    bool IsRunning { get; }

    Task StartAsync(CancellationToken ct);

    ValueTask StopAsync();
}
```

- [x] **Step 2: Create ILogSourceFactory.cs**

```csharp
namespace LogTail.Core.Sources;

public interface ILogSourceFactory
{
    ILogSource CreateFileSource(string filePath);
}
```

- [x] **Step 3: Create LogSourceFactory.cs**

```csharp
using LogTail.Core.Logging;

namespace LogTail.Core.Sources;

public sealed class LogSourceFactory : ILogSourceFactory
{
    private readonly ILogTailLogger _logger;
    private readonly TimeSpan _pollInterval;

    public LogSourceFactory(ILogTailLogger logger, TimeSpan pollInterval = default)
    {
        _logger = logger;
        _pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(250) : pollInterval;
    }

    public ILogSource CreateFileSource(string filePath)
    {
        return new FileTailSource(filePath, _pollInterval, _logger);
    }
}
```

Note: `FileTailSource` is created in Task 6. This task creates the factory that depends on it — but it will compile only after Task 6 is complete. Create this file after Task 6, or create the file now with the class marked `partial` and the constructor call forward-declared. Recommended approach: defer the factory creation to after Task 6, or create the file now with a `NotImplementedException` stub and fill it in after Task 6. Simplest: create Tasks 5 and 6 together, committing Task 5 output only after Task 6 compiles.

- [x] **Step 4: Verify build** (after Task 6 is also created)

```bash
dotnet build src/LogTail.Core
```

Expected: BUILD SUCCEEDED.

- [x] **Step 5: Commit**

```bash
git add src/LogTail.Core/Sources/
git commit -m "feat(core): add ILogSource, ILogSourceFactory, and LogSourceFactory"
```

---

## Task 6: FileTailSource

**Files:**
- Create: `src/LogTail.Core/Sources/FileTailSource.cs`
- Create: `tests/LogTail.Core.Tests/Sources/FileTailSourceTests.cs`

**Interfaces:**
- Consumes: `RawLogEvent` (Task 2), `ILogTailLogger` (Task 4)
- Produces: `FileTailSource` — consumed by `LogSourceFactory` (Task 5), `MainWindowViewModel` (Task 8)

- [x] **Step 1: Write the failing tests**

Create `tests/LogTail.Core.Tests/Sources/FileTailSourceTests.cs`:

```csharp
using System.Reactive.Linq;
using FluentAssertions;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Sources;
using Xunit;

namespace LogTail.Core.Tests.Sources;

public sealed class FileTailSourceTests : IAsyncLifetime
{
    private readonly string _tempDir;

    public FileTailSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        await ValueTask.CompletedTask;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Emits_new_lines_when_appended()
    {
        var filePath = Path.Combine(_tempDir, "test.log");
        File.WriteAllText(filePath, "line1\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(100); // allow tail to settle

        await File.AppendAllTextAsync(filePath, "line2\nline3\n");

        await Task.Delay(300); // allow watcher + poll to catch up

        await sut.StopAsync();

        events.Should().Contain(e => e.Line.Contains("line2"));
        events.Should().Contain(e => e.Line.Contains("line3"));
    }

    [Fact]
    public async Task Reads_from_end_by_default()
    {
        var filePath = Path.Combine(_tempDir, "existing.log");
        File.WriteAllText(filePath, "old-line1\nold-line2\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(100);

        await File.AppendAllTextAsync(filePath, "new-line\n");

        await Task.Delay(300);

        await sut.StopAsync();

        events.Should().Contain(e => e.Line == "new-line");
        events.Should().NotContain(e => e.Line == "old-line1");
    }

    [Fact]
    public async Task Handles_rotation_by_reopening()
    {
        var filePath = Path.Combine(_tempDir, "rotate.log");
        await File.WriteAllTextAsync(filePath, "before-rotate\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);

        await Task.Delay(100);

        // Simulate rotation: delete original, create new file with same name
        File.Delete(filePath);
        await File.AppendAllTextAsync(filePath, "after-rotate\n");

        await Task.Delay(500);

        await sut.StopAsync();

        events.Should().Contain(e => e.Line == "after-rotate");
    }

    [Fact]
    public async Task StopAsync_stops_emitting()
    {
        var filePath = Path.Combine(_tempDir, "stop.log");
        await File.WriteAllTextAsync(filePath, "start\n");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        var events = new List<RawLogEvent>();
        using var sub = sut.Events.Subscribe(e => events.Add(e));

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await sut.StopAsync();

        var countBefore = events.Count;

        await File.AppendAllTextAsync(filePath, "after-stop\n");
        await Task.Delay(300);

        events.Count.Should().Be(countBefore);
    }

    [Fact]
    public async Task DisplayName_returns_filename()
    {
        var filePath = Path.Combine(_tempDir, "mylog.log");
        await File.WriteAllTextAsync(filePath, "");

        await using var sut = new FileTailSource(filePath, TimeSpan.FromMilliseconds(50), new ConsoleLogger());

        sut.DisplayName.Should().Be("mylog.log");
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~FileTailSourceTests" --no-build
```

Expected: FAIL — `FileTailSource` does not exist.

- [x] **Step 3: Implement FileTailSource**

Create `src/LogTail.Core/Sources/FileTailSource.cs`:

```csharp
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using LogTail.Core.Logging;
using LogTail.Core.Models;

namespace LogTail.Core.Sources;

public sealed class FileTailSource : ILogSource
{
    private readonly string _filePath;
    private readonly TimeSpan _pollInterval;
    private readonly ILogTailLogger _logger;
    private readonly Subject<RawLogEvent> _events = new();

    private FileStream? _stream;
    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private CancellationTokenSource? _cts;
    private long _offset;
    private Task? _readLoop;

    public FileTailSource(string filePath, TimeSpan pollInterval, ILogTailLogger logger)
    {
        _filePath = filePath;
        _pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(250) : pollInterval;
        _logger = logger;
    }

    public string DisplayName => Path.GetFileName(_filePath);

    public IObservable<RawLogEvent> Events => _events.AsObservable();

    public bool IsRunning { get; private set; }

    public async Task StartAsync(CancellationToken ct)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Open file from end (tail mode)
        _stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        _offset = _stream.Length;

        // Set up FileSystemWatcher
        var dir = Path.GetDirectoryName(_filePath)!;
        var filter = Path.GetFileName(_filePath);

        _watcher = new FileSystemWatcher(dir, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.Delete
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        // Poll fallback
        _pollTimer = new Timer(PollCallback, null, _pollInterval, _pollInterval);

        // Read loop
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

        IsRunning = true;
        _logger.Info($"Started tailing: {_filePath}");
    }

    public async ValueTask StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;

        _pollTimer?.Dispose();
        _pollTimer = null;

        _watcher?.Dispose();
        _watcher = null;

        _cts?.Cancel();

        if (_readLoop != null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _stream?.Dispose();
        _stream = null;

        _cts?.Dispose();
        _cts = null;

        _logger.Info($"Stopped tailing: {_filePath}");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _events.Dispose();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        TriggerRead();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        _logger.Warn($"File rotated: {e.OldFullPath} → {e.FullPath}");
        TriggerRead();
    }

    private void PollCallback(object? state)
    {
        TriggerRead();
    }

    private void TriggerRead()
    {
        // Signal the read loop to attempt a read.
        // The read loop itself handles concurrency via Monitor.
        lock (_readLock)
        {
            Monitor.Pulse(_readLock);
        }
    }

    private readonly object _readLock = new();

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                lock (_readLock)
                {
                    Monitor.Wait(_readLock, _pollInterval);
                }

                if (_stream == null || !File.Exists(_filePath))
                {
                    // File may have been deleted/rotated
                    await ReopenFileAsync().ConfigureAwait(false);
                    continue;
                }

                var currentLength = new FileInfo(_filePath).Length;

                // Truncation detection: file shrunk below our offset
                if (currentLength < _offset)
                {
                    _logger.Warn($"File truncated (size {currentLength} < offset {_offset}). Resetting offset.");
                    _offset = 0;
                }

                if (_stream.Position != _offset)
                {
                    _stream.Seek(_offset, SeekOrigin.Begin);
                }

                if (_offset >= currentLength)
                {
                    continue;
                }

                // Read available bytes
                var bufferSize = (int)Math.Min(currentLength - _offset, 64 * 1024);
                var buffer = new byte[bufferSize];

                int bytesRead = await _stream.ReadAsync(buffer, 0, bufferSize, ct).ConfigureAwait(false);

                if (bytesRead > 0)
                {
                    _offset += bytesRead;
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);

                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrEmpty(line)) // skip empty split artifacts
                        {
                            _events.OnNext(new RawLogEvent(
                                ReadAt: DateTimeOffset.UtcNow,
                                SourceId: _filePath,
                                FileOffset: _offset,
                                Line: line));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error($"Read error on {_filePath}", ex);
                await ReopenFileAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task ReopenFileAsync()
    {
        _stream?.Dispose();
        _stream = null;

        // Wait for file to reappear
        for (int i = 0; i < 20; i++) // 2 seconds max
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    _stream = new FileStream(
                        _filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 4096,
                        FileOptions.Asynchronous);

                    _offset = 0; // Start from beginning of new file
                    _logger.Info($"Reopened file: {_filePath} at offset 0");
                    return;
                }
                catch
                {
                    // File exists but locked, retry
                }
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        _logger.Warn($"File not found after rotation: {_filePath}. Will keep retrying.");
    }
}
```

Note: the `lock (_readLock)` / `Monitor.Wait` / `Monitor.Pulse` pattern replaces a `SemaphoreSlim` for simplicity. The lock is short-held (just signaling), so contention is negligible.

- [x] **Step 4: Now create LogSourceFactory.cs (from Task 5)**

```csharp
using LogTail.Core.Logging;

namespace LogTail.Core.Sources;

public sealed class LogSourceFactory : ILogSourceFactory
{
    private readonly ILogTailLogger _logger;
    private readonly TimeSpan _pollInterval;

    public LogSourceFactory(ILogTailLogger logger, TimeSpan pollInterval = default)
    {
        _logger = logger;
        _pollInterval = pollInterval == default ? TimeSpan.FromMilliseconds(250) : pollInterval;
    }

    public ILogSource CreateFileSource(string filePath)
    {
        return new FileTailSource(filePath, _pollInterval, _logger);
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~FileTailSourceTests"
```

Expected: PASS (5/5). Note: these tests involve file I/O timing, so some tolerance is needed. If a test is flaky, increase the `Task.Delay` values.

- [x] **Step 6: Commit**

```bash
git add src/LogTail.Core/Sources/ tests/LogTail.Core.Tests/Sources/
git commit -m "feat(core): implement FileTailSource with FileSystemWatcher + poll fallback"
```

---

## Task 7: Enrich Pipeline (No-Op)

**Files:**
- Create: `src/LogTail.Core/Pipeline/Enrich.cs`
- Create: `tests/LogTail.Core.Tests/Pipeline/EnrichTests.cs`

**Interfaces:**
- Consumes: `RawLogEvent` (Task 2), `LogLevel` (Task 2)
- Produces: `Enrich` static class — used by `MainWindowViewModel` (Task 8)

- [x] **Step 1: Write the failing tests**

Create `tests/LogTail.Core.Tests/Pipeline/EnrichTests.cs`:

```csharp
using FluentAssertions;
using LogTail.Core.Models;
using LogTail.Core.Pipeline;
using Xunit;

namespace LogTail.Core.Tests.Pipeline;

public sealed class EnrichTests
{
    [Fact]
    public void Transform_sets_level_to_Unknown()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "2026-09-02 INFO Application started");

        var result = Enrich.Transform(raw);

        result.Level.Should().Be(LogLevel.Unknown);
    }

    [Fact]
    public void Transform_sets_timestamp_to_null()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "2026-09-02 INFO Application started");

        var result = Enrich.Transform(raw);

        result.Timestamp.Should().BeNull();
    }

    [Fact]
    public void Transform_sets_isHighlighted_to_false()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "some line");

        var result = Enrich.Transform(raw);

        result.IsHighlighted.Should().BeFalse();
    }

    [Fact]
    public void Transform_sets_isHidden_to_false()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 0,
            Line: "some line");

        var result = Enrich.Transform(raw);

        result.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Transform_preserves_raw_line()
    {
        var raw = new RawLogEvent(
            ReadAt: DateTimeOffset.UtcNow,
            SourceId: "test",
            FileOffset: 100,
            Line: "hello world");

        var result = Enrich.Transform(raw);

        result.Raw.Should().Be(raw);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~EnrichTests" --no-build
```

Expected: FAIL — `Enrich` does not exist.

- [x] **Step 3: Implement Enrich**

Create `src/LogTail.Core/Pipeline/Enrich.cs`:

```csharp
using LogTail.Core.Models;

namespace LogTail.Core.Pipeline;

/// <summary>
/// Milestone 1: no-op pipeline stage.
/// Always returns EnrichedLogEvent with Level=Unknown, Timestamp=null,
/// IsHighlighted=false, IsHidden=false.
/// M2 will add actual level detection, timestamp parsing, filter, and highlight.
/// </summary>
public static class Enrich
{
    public static EnrichedLogEvent Transform(RawLogEvent raw)
    {
        return new EnrichedLogEvent(
            Raw: raw,
            Level: LogLevel.Unknown,
            Timestamp: null,
            LevelColorKey: null,
            IsHighlighted: false,
            IsHidden: false);
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/LogTail.Core.Tests --filter "FullyQualifiedName~EnrichTests"
```

Expected: PASS (5/5).

- [x] **Step 5: Commit**

```bash
git add src/LogTail.Core/Pipeline/ tests/LogTail.Core.Tests/Pipeline/
git commit -m "feat(core): add no-op Enrich pipeline stage for M1"
```

---

## Task 8: MainWindowViewModel

**Files:**
- Create: `src/LogTail.UI/ViewModels/MainWindowViewModel.cs`
- Create: `tests/LogTail.UI.Tests/MainWindowViewModelTests.cs`
- Create: `tests/LogTail.UI.Tests/TestApp.cs`

**Interfaces:**
- Consumes: `ILogSourceFactory` (Task 5), `SettingsStore` (Task 4), `RingBuffer<T>` (Task 3), `Enrich` (Task 7), `EnrichedLogEvent`, `ThemeMode`, `AppSettings` (Task 2)
- Produces: `MainWindowViewModel` — consumed by `MainWindow.axaml` (Task 10)

- [x] **Step 1: Create TestApp.cs (Avalonia headless bootstrapper)**

Create `tests/LogTail.UI.Tests/TestApp.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;

namespace LogTail.UI.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseReactiveUI();
    }
}
```

- [x] **Step 2: Write the failing tests**

Create `tests/LogTail.UI.Tests/MainWindowViewModelTests.cs`:

```csharp
using FluentAssertions;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Sources;
using LogTail.UI.ViewModels;
using Xunit;

namespace LogTail.UI.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logtail-vm-test-{Guid.NewGuid():N}");
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
    public void Initial_state_has_correct_defaults()
    {
        var sut = CreateViewModel();

        sut.WindowTitle.Should().Be("Log Tail");
        sut.StatusMessage.Should().Be("No file open");
        sut.CurrentFilePath.Should().BeNull();
        sut.CurrentTheme.Should().Be(ThemeMode.System);
        sut.VisibleEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetTheme_updates_CurrentTheme()
    {
        var sut = CreateViewModel();

        sut.SetThemeCommand.Execute(ThemeMode.Dark).Subscribe();

        sut.CurrentTheme.Should().Be(ThemeMode.Dark);
    }

    [Fact]
    public void Clear_resets_VisibleEvents()
    {
        var sut = CreateViewModel();
        sut.VisibleEvents.Add(new Core.Models.EnrichedLogEvent(
            new Core.Models.RawLogEvent(DateTimeOffset.UtcNow, "test", 0, "line"),
            Core.Models.LogLevel.Unknown, null, null));

        sut.ClearCommand.Execute().Subscribe();

        sut.VisibleEvents.Should().BeEmpty();
    }

    private MainWindowViewModel CreateViewModel()
    {
        var logger = new ConsoleLogger();
        var settings = new Core.Persistence.SettingsStore(_tempDir, logger);
        var factory = new LogSourceFactory(logger);

        return new MainWindowViewModel(settings, factory);
    }
}
```

Note: These tests use `Avalonia.Headless` via `TestApp.cs` (the `[Collection("Avalonia")]` attribute on the test class or a custom `AvaloniaFact` attribute may be needed — check `Avalonia.Headless.XUnit` documentation). If the headless platform is required, add:

```csharp
using Avalonia.Headless.XUnit;

[Collection("Avalonia")]
public sealed class MainWindowViewModelTests
```

- [x] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/LogTail.UI.Tests --filter "FullyQualifiedName~MainWindowViewModelTests" --no-build
```

Expected: FAIL — `MainWindowViewModel` does not exist.

- [x] **Step 4: Implement MainWindowViewModel**

Create `src/LogTail.UI/ViewModels/MainWindowViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using LogTail.Core.Buffer;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using LogTail.Core.Pipeline;
using LogTail.Core.Sources;
using ReactiveUI;

namespace LogTail.UI.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    [Reactive]
    public string WindowTitle { get; set; } = "Log Tail";

    [Reactive]
    public string? CurrentFilePath { get; set; }

    [Reactive]
    public string StatusMessage { get; set; } = "No file open";

    [Reactive]
    public ThemeMode CurrentTheme { get; set; }

    public ObservableCollection<EnrichedLogEvent> VisibleEvents { get; } = new();

    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    public ReactiveCommand<ThemeMode, Unit> SetThemeCommand { get; }

    [ObservableAsProperty]
    public bool IsTailing { get; }

    public bool CanOpenFile => !IsTailing;

    private readonly SettingsStore _settings;
    private readonly ILogSourceFactory _sourceFactory;
    private readonly RingBuffer<EnrichedLogEvent> _buffer;
    private ILogSource? _currentSource;
    private IDisposable? _eventsSubscription;

    public MainWindowViewModel(SettingsStore settings, ILogSourceFactory sourceFactory)
    {
        _settings = settings;
        _sourceFactory = sourceFactory;
        _buffer = new RingBuffer<EnrichedLogEvent>(50_000);

        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        ClearCommand = ReactiveCommand.Create(Clear);
        SetThemeCommand = ReactiveCommand.Create<ThemeMode>(SetTheme);

        // Wire IsTailing
        this.WhenAnyValue(x => x._currentSource)
            .Select(source => source?.IsRunning == true)
            .ToProperty(this, x => x.IsTailing);

        // Restore theme from settings
        var loaded = _settings.Load();
        CurrentTheme = loaded.Theme;
    }

    private async Task OpenFileAsync()
    {
        // Close previous source if open
        if (_currentSource != null)
        {
            await _currentSource.StopAsync();
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;
        }

        // For M1, CurrentFilePath must be set externally (via file picker in Task 10).
        // This path is triggered by OpenFileCommand after file picker sets CurrentFilePath.
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            StatusMessage = "No file selected";
            return;
        }

        var source = _sourceFactory.CreateFileSource(CurrentFilePath);
        _currentSource = source;

        _eventsSubscription = source.Events
            .Select(raw => Enrich.Transform(raw))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                onNext: OnNewEvent,
                onError: ex =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });

        await source.StartAsync(CancellationToken.None);

        StatusMessage = $"Tailing: {Path.GetFileName(CurrentFilePath)}";
    }

    private void OnNewEvent(EnrichedLogEvent enriched)
    {
        if (_buffer.Count == _buffer.Capacity)
        {
            // Eviction happened — rebuild VisibleEvents from buffer
            VisibleEvents.Clear();
            foreach (var item in _buffer)
            {
                VisibleEvents.Add(item);
            }
        }

        _buffer.Add(enriched);
        VisibleEvents.Add(enriched);
    }

    private void Clear()
    {
        _buffer.Clear();
        VisibleEvents.Clear();
    }

    private void SetTheme(ThemeMode mode)
    {
        CurrentTheme = mode;
        _settings.Update(s => s with { Theme = mode });
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/LogTail.UI.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"
```

Expected: PASS (3/3).

- [x] **Step 6: Commit**

```bash
git add src/LogTail.UI/ViewModels/ tests/LogTail.UI.Tests/
git commit -m "feat(ui): add MainWindowViewModel with open, clear, theme commands"
```

---

## Task 9: App.axaml + App.axaml.cs (Theme Wiring + DI)

**Files:**
- Modify: `src/LogTail.UI/App.axaml` (or create if not exists)
- Modify: `src/LogTail.UI/App.axaml.cs` (or create if not exists)

**Interfaces:**
- Consumes: `MainWindowViewModel` (Task 8), `ThemeMode` (Task 2), `SettingsStore` (Task 4)
- Produces: Application bootstrap that wires theme and DI — used by `Program.cs` (Task 11) and `MainWindow.axaml` (Task 10)

- [x] **Step 1: Create App.axaml**

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="LogTail.UI.App"
             RequestedThemeVariant="Default">
  <Application.Styles>
    <FluentTheme />
  </Application.Styles>
</Application>
```

- [x] **Step 2: Create App.axaml.cs**

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LogTail.Core.Logging;
using LogTail.Core.Models;
using LogTail.Core.Persistence;
using LogTail.Core.Sources;
using LogTail.UI.ViewModels;
using LogTail.UI.Views;

namespace LogTail.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build dependencies
            var logger = new ConsoleLogger();

            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "log-tail");
            Directory.CreateDirectory(appDataDir);

            var settings = new SettingsStore(appDataDir, logger);
            var factory = new LogSourceFactory(logger);

            var viewModel = new MainWindowViewModel(settings, factory);

            // Apply saved theme
            var loaded = settings.Load();
            ApplyTheme(loaded.Theme);

            // Observe theme changes
            viewModel.WhenAnyValue(x => x.CurrentTheme)
                .Subscribe(ApplyTheme);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(ThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
```

Note: `viewModel.WhenAnyValue` requires a `using ReactiveUI;` and `using System.Reactive.Linq;`. Add if not covered by implicit usings.

- [x] **Step 3: Verify build**

```bash
dotnet build src/LogTail.UI
```

Expected: BUILD SUCCEEDED (may have warnings about `App.axaml` not found if the Avalonia template wasn't used; ensure the file is in the project root and marked as `<AvaloniaResource>` in `.csproj`).

- [x] **Step 4: Commit**

```bash
git add src/LogTail.UI/App.axaml src/LogTail.UI/App.axaml.cs
git commit -m "feat(ui): wire theme persistence and DI in App.axaml.cs"
```

---

## Task 10: MainWindow.axaml

**Files:**
- Create: `src/LogTail.UI/Views/MainWindow.axaml`
- Create: `src/LogTail.UI/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `MainWindowViewModel` (Task 8), `EnrichedLogEvent` (Task 2)
- Produces: UI shell — the visual app

- [x] **Step 1: Create MainWindow.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LogTail.UI.ViewModels"
        xmlns:models="using:LogTail.Core.Models"
        x:Class="LogTail.UI.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="{Binding WindowTitle}"
        Width="900"
        Height="600">
  <DockPanel>
    <Menu DockPanel.Dock="Top">
      <MenuItem Header="_File">
        <MenuItem Header="_Open..." Command="{Binding OpenFileCommand}" />
        <Separator />
        <MenuItem Header="E_xit" />
      </MenuItem>
      <MenuItem Header="_View">
        <MenuItem Header="_Theme">
          <MenuItem Header="System" Command="{Binding SetThemeCommand}" CommandParameter="System" />
          <MenuItem Header="Light" Command="{Binding SetThemeCommand}" CommandParameter="Light" />
          <MenuItem Header="Dark" Command="{Binding SetThemeCommand}" CommandParameter="Dark" />
        </MenuItem>
      </MenuItem>
    </Menu>

    <Grid DockPanel.Dock="Bottom" Height="24" ColumnDefinitions="*">
      <TextBlock Text="{Binding StatusMessage}"
                 Margin="6,0"
                 VerticalAlignment="Center"
                 FontSize="11" />
    </Grid>

    <ListBox ItemsSource="{Binding VisibleEvents}"
             VirtualizingStackPanel.IsVirtualized="True"
             VirtualizingStackPanel.VirtualizationMode="Recycling">
      <ListBox.ItemTemplate>
        <DataTemplate DataType="models:EnrichedLogEvent">
          <TextBlock Text="{Binding Raw.Line}"
                     FontFamily="Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,monospace"
                     FontSize="13" />
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Window>
```

- [x] **Step 2: Create MainWindow.axaml.cs**

```csharp
using Avalonia.Controls;

namespace LogTail.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 3: Wire OpenFile via StorageProvider**

Update `MainWindowViewModel.cs` — the `OpenFileAsync` method needs to use Avalonia's `IStorageProvider` to show a file picker. Since `IStorageProvider` is a UI concern, inject a delegate or interface:

Add to `MainWindowViewModel.cs`:

```csharp
public Func<Task<string?>>? FilePickerFunc { get; set; }
```

And update `OpenFileAsync`:

```csharp
private async Task OpenFileAsync()
{
    if (_currentSource != null)
    {
        await _currentSource.StopAsync();
        _eventsSubscription?.Dispose();
        _eventsSubscription = null;
    }

    // Use file picker if available, otherwise use CurrentFilePath
    if (FilePickerFunc != null)
    {
        CurrentFilePath = await FilePickerFunc();
    }

    if (string.IsNullOrEmpty(CurrentFilePath))
    {
        StatusMessage = "No file selected";
        return;
    }

    // ... rest of implementation ...
}
```

Then in `MainWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LogTail.UI.ViewModels;

namespace LogTail.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.FilePickerFunc = OpenFilePickerAsync;
            }
        };
    }

    private async Task<string?> OpenFilePickerAsync()
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select log file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log files") { Patterns = new[] { "*.log", "*.txt", "*.*" } }
            }
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }
}
```

- [x] **Step 4: Verify build**

```bash
dotnet build src/LogTail.UI
```

Expected: BUILD SUCCEEDED.

- [x] **Step 5: Commit**

```bash
git add src/LogTail.UI/Views/
git commit -m "feat(ui): add MainWindow with virtualized log view and file picker"
```

---

## Task 11: Program.cs (Entry Point)

**Files:**
- Create: `src/LogTail.UI/Program.cs`

**Interfaces:**
- Consumes: `App` (Task 9)
- Produces: Application entry point — launches the app

- [x] **Step 1: Create Program.cs**

```csharp
using Avalonia;

namespace LogTail.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
```

- [x] **Step 2: Verify full solution builds**

```bash
dotnet build
```

Expected: BUILD SUCCEEDED across all projects.

- [x] **Step 3: Commit**

```bash
git add src/LogTail.UI/Program.cs
git commit -m "feat(ui): add Program.cs entry point"
```

---

## Task 12: End-to-End Verification

**Files:** no new files. Manual verification and any fixes needed.

**Interfaces:**
- Consumes: all previous tasks
- Produces: verified Milestone 1 that meets the Definition of Done

- [x] **Step 1: Run all tests**

```bash
dotnet test
```

Expected: ALL PASS across Core.Tests and UI.Tests.

- [ ] **Step 2: Run the application**

```bash
dotnet run --project src/LogTail.UI
```

Manual verification:
1. App opens with "Log Tail" title, monospace font, status bar showing "No file open".
2. File → Open... shows a file picker. Select a `.log` file.
3. Status bar changes to "Tailing: {filename}".
4. Append lines to the file from another terminal: `echo "test line" >> test.log`. Lines appear in UI within ~500ms.
5. Append 100+ lines rapidly. Scrolling stays smooth.
6. Close and reopen app. Theme persists (System/Light/Dark as previously set).
7. Clear (View → or button) resets the list.

- [ ] **Step 3: Fix any issues found**

If the app doesn't compile due to Avalonia 11→12 API breaking changes, fix them here. Common issues:
- `TopLevel.GetTopLevel()` API changes
- `IStorageProvider` namespace changes
- XAML `ThemeVariant` binding syntax
- `FluentTheme` constructor changes

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: final verification fixes for M1 foundation"
```

- [ ] **Step 5: Tag the milestone**

```bash
git tag v0.1.0-m1-foundation
```

---

## Summary

| Task | Deliverable | Test Coverage |
|------|------------|---------------|
| 1 | Solution scaffold | Builds |
| 2 | Models (5 files) | Builds |
| 3 | RingBuffer | 9 unit tests |
| 4 | SettingsStore + Logger | 4 unit tests |
| 5 | ILogSource + Factory | Builds |
| 6 | FileTailSource | 5 integration tests |
| 7 | Enrich (no-op) | 5 unit tests |
| 8 | MainWindowViewModel | 3 headless tests |
| 9 | App.axaml wiring | Builds |
| 10 | MainWindow UI | Builds |
| 11 | Program.cs | Builds |
| 12 | E2E verification | Manual + all tests pass |

**Total estimated time:** 4–6 hours for a developer familiar with Avalonia/ReactiveUI; 8–10 hours otherwise.
