# NuGet

Covers `NuGetMirror`, `NuGetBundleBuilder`, `NuGetBundleInstaller`, `NuGetFeed`, `NuGetLayout`, `NuGetLicenses`, `NuGetPackageList`, `TemplatePatcher` and `DotNetSdks`: an offline package feed that behaves like nuget.org.

## Mirror

`DotNetSdks` downloads the latest SDK of each supported channel (10.0 and 11.0) into the cache and runs them privately. `NuGetMirror` restores, once per SDK, a scratch project for that SDK's own target framework (net10.0 with the 10 SDK, net11.0 with the 11 SDK) that references the requested packages (latest stable unless `Id@version` for one exact version, `Id@10.*` for the newest stable 10.x one, or `Id@dotnet` for the newest stable one that goes with the target framework being restored, 10.x for net10.0 and 11.x for net11.0, which packages that follow the .NET release use because a net10.0 project cannot take their 11.x versions; `?Id` optional; `=Id` without dependencies), plus the runtime packs for linux-x64 and win-x64. Every restore lands in the same package folder, so the feed is the union of both. Then it copies every package in the global packages folder into the feed layout `id/version/id.version.nupkg` with `.sha512` and `.nuspec`: NuGet only treats a folder as a hierarchical feed when those hash files exist. The scratch folder is outside any git repository so a repository's `global.json` or `Directory.Build.props` cannot change the restore.

## Feed on the offline computer

`/opt/bundler/nuget-feed` is shared by all NuGet bundles. Installing adds only the versions the feed does not have; older versions stay, and only versions the bundle itself added are removed on uninstall. A version two bundles both ship is recorded by the second one as shared; when the first is uninstalled the version is handed over to the second, so it stays until that one goes too. The `dotnet` bundle writes machine-wide and per-user `NuGet.Config` files that list only this feed.

## Templates

Template packs (such as `xunit.v3.templates`) stay in the bundle's `templates/` folder and are installed with `dotnet new install`, compared by version like any service. `TemplatePatcher` rewrites a pack at build time: it removes the `PackageReference` of packages whose license is unclear (`AvaloniaUI.DiagnosticsSupport`) and the calls that use them, and fails the build when something it was asked to remove is no longer in the pack, because that means the pack changed.

## Licenses

`NuGetLicenses` reads each package's `.nuspec`: an SPDX expression made of permissive licenses is OK, everything else (license file, bare URL, other expression) is REVIEW unless the id is in `licenses/reviewed-nuget.tsv`, which lists packages a person has read. The report is stored in the bundle.
