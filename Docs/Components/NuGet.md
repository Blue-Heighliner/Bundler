# NuGet

Covers `NuGetMirror`, `NuGetBundleBuilder`, `NuGetBundleInstaller`, `NuGetFeed`, `NuGetLayout`, `NuGetLicenses`, `NuGetPackageList`, `TemplatePatcher` and `DotNetSdks`: an offline package feed that behaves like nuget.org.

## Mirror

`DotNetSdks` downloads the latest stable .NET SDK into the cache and runs it privately. `NuGetMirror` restores a scratch project that references the requested packages (latest stable unless `Id@version`; `?Id` optional; `=Id` without dependencies) for `net10.0`, plus the runtime packs for linux-x64 and win-x64, then copies every package in the resulting global packages folder into the feed layout `id/version/id.version.nupkg` with `.sha512` and `.nuspec`: NuGet only treats a folder as a hierarchical feed when those hash files exist. The scratch folder is outside any git repository so a repository's `global.json` or `Directory.Build.props` cannot change the restore.

## Feed on the offline computer

`/opt/bundler/nuget-feed` is shared by all NuGet bundles. Installing adds only the versions the feed does not have; older versions stay, and only versions the bundle itself added are removed on uninstall. A version two bundles both ship is recorded by the second one as shared; when the first is uninstalled the version is handed over to the second, so it stays until that one goes too. The `dev` bundle writes machine-wide and per-user `NuGet.Config` files that list only this feed.

## Templates

Template packs (such as `xunit.v3.templates`) stay in the bundle's `templates/` folder and are installed with `dotnet new install`, compared by version like any service. `TemplatePatcher` rewrites a pack at build time: it removes the `PackageReference` of packages whose license is unclear (`AvaloniaUI.DiagnosticsSupport`) and the calls that use them, and fails the build when something it was asked to remove is no longer in the pack, because that means the pack changed.

## Licenses

`NuGetLicenses` reads each package's `.nuspec`: an SPDX expression made of permissive licenses is OK, everything else (license file, bare URL, other expression) is REVIEW unless the id is in `licenses/reviewed-nuget.tsv`, which lists packages a person has read. The report is stored in the bundle.
