[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]
[assembly: AvaloniaTestApplication(typeof(BlueHeighliner.Bundler.Tests.TestApplication))]

namespace BlueHeighliner.Bundler.Tests;

public sealed class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
