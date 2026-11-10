namespace BlueHeighliner.Bundler;

/// <summary>
/// Registers everything the app is made of.
/// </summary>
internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the services of the engine, the bundle definitions and the log.
        /// </summary>
        /// <param name="bundlesDirectory">The folder with the bundle files, or <see langword="null" /> for <c>Bundles</c> next to the executable.</param>
        /// <param name="log">The log lines go to: <see cref="ConsoleLog" /> for the command line, the window's own log otherwise.</param>
        /// <returns>The same collection.</returns>
        public IServiceCollection AddBundler(Func<IServiceProvider, ILog> log, string? bundlesDirectory = null)
        {
            services.AddSingleton(provider => log(provider));
            services.AddSingleton<ILogFormat, LogFormat>();
            services.AddSingleton<IMachine, Machine>();
            services.AddSingleton<IPaths>(provider => new Paths(provider.GetRequiredService<IMachine>(), bundlesDirectory));
            services.AddSingleton<IResources, Resources>();
            services.AddSingleton<IHashing, Hashing>();
            services.AddSingleton<IVersions, Versions>();
            services.AddSingleton<IDecisions, Decisions>();
            services.AddSingleton<IProcessRunner, ProcessRunner>();
            services.AddSingleton<ISystemd, Systemd>();
            services.AddSingleton<IUsers, Users>();
            services.AddSingleton<IUserFiles, UserFiles>();
            services.AddSingleton<IJsonFiles, JsonFiles>();
            services.AddSingleton<ITextBlocks, TextBlocks>();
            services.AddSingleton<IStateStore, StateStore>();
            services.AddSingleton<IBundleArchive, BundleArchive>();
            services.AddSingleton<IBundleFiles, BundleFiles>();
            services.AddSingleton(new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestHeaders = { { "User-Agent", "bundler" } } });
            services.AddSingleton<IHttpFetcher, HttpFetcher>();
            services.AddSingleton<IDownloads, Downloads>();
            services.AddSingleton<IReleaseSources, ReleaseSources>();
            services.AddSingleton<IMarketplace, Marketplace>();
            services.AddSingleton<IOllamaRegistry, OllamaRegistry>();
            services.AddSingleton<IDebIndex, DebIndex>();
            services.AddSingleton<IAptBuilder, AptBuilder>();
            services.AddSingleton<IAptInstaller, AptInstaller>();
            services.AddSingleton<IDotNetSdks, DotNetSdks>();
            services.AddSingleton<INuGetPackageList, NuGetPackageList>();
            services.AddSingleton<INuGetLayout, NuGetLayout>();
            services.AddSingleton<INuGetLicenses, NuGetLicenses>();
            services.AddSingleton<ITemplatePatcher, TemplatePatcher>();
            services.AddSingleton<INuGetMirror, NuGetMirror>();
            services.AddSingleton<INuGetFeed, NuGetFeed>();
            services.AddSingleton<INuGetBundleBuilder, NuGetBundleBuilder>();
            services.AddSingleton<INuGetBundleInstaller, NuGetBundleInstaller>();
            services.AddSingleton<IBundle, GitBundle>();
            services.AddSingleton<IBundle, A2000Bundle>();
            services.AddSingleton<IBundle, DotNetBundle>();
            services.AddSingleton<IBundle, OllamaBundle>();
            services.AddSingleton<IBundle, VsCodeBundle>();
            services.AddSingleton<IBundle, GemmaBundle>();
            services.AddSingleton<IBundle, OpenCodeBundle>();
            services.AddSingleton<IOpenCodeFiles, OpenCodeFiles>();
            services.AddSingleton<IBundle, XunitBundle>();
            services.AddSingleton<IBundle, AvaloniaBundle>();
            services.AddSingleton<IBundle, ProtobufBundle>();
            services.AddSingleton<IBundle, SqliteBundle>();
            services.AddSingleton<IBundle, GodotBundle>();
            services.AddSingleton<IBundle, PodmanBundle>();
            services.AddSingleton<IBundle, GimpBundle>();
            services.AddSingleton<IBundle, KritaBundle>();
            services.AddSingleton<IBundle, BlenderBundle>();
            services.AddSingleton<IBundle, AutoRemoteBundle>();
            services.AddSingleton<IAutoRemoteFiles, AutoRemoteFiles>();
            services.AddSingleton<IBundleCatalog, BundleCatalog>();
            services.AddSingleton<ILicenseTexts, LicenseTexts>();
            services.AddSingleton<IBundleEngine, BundleEngine>();
            services.AddSingleton<IElevator, Elevator>();
            services.AddSingleton<ICommandLine, CommandLine>();
            return services;
        }
    }
}
