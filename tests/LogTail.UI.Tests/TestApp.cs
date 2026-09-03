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
