# Bundles

Every bundle Bundler can build and install. Each has a Target, the system it is made for; all of them target Ubuntu 24.04 and an install on another system only warns (the Ubuntu packages of a bundle fit the release it was built for, and apt refuses an install that would change or remove packages that are on the computer). A bundle is built on an online computer into one file, `<name>.bundle`, in the `Bundles` folder next to the app; that file is copied into the `Bundles` folder next to the app on the offline Ubuntu 24.04 computer. A build downloads the latest stable release of each component, so a file always holds the versions that were latest when it was built. Bundles have no version numbers of their own, only the versions of the services inside them (the Version column below, which are the ones of the files built between 2026-10-28 and 2026-11-10). Whether a file is newer than what is installed is for the person to judge, from when the file was last changed; moving to it means reinstalling the bundle, which uninstalls it and installs the new file.

Scope: internal use inside one organization, on machines it owns, nothing redistributed (see `AGENTS.md`). **License** lists the licenses by short name; in the app, clicking one opens its full text (`licenses/texts/`). The text of a standard license is the standard text, the copyright line of each project is in its package. **Usage** says "Free use" when the licenses put no restriction on that scope, otherwise it lists the restrictions. "License review" below says where the licenses were read. This is not legal advice.

How installs treat what is already on the computer: a missing component is installed, an older one is replaced, the same version is skipped, and a newer one is kept (it is never replaced by an older one). NuGet packages are only ever added: older versions stay in the feed. An uninstall removes only what the bundle itself installed or replaced.

| Bundle | Needs | Installs |
|---|---|---|
| [`git`](#git) | none | git |
| [`a2000`](#a2000) | none | NVIDIA driver for the RTX A2000 |
| [`dotnet`](#dotnet) | none | .NET 10 and 11 SDKs, offline NuGet feed |
| [`ollama`](#ollama) | none | Ollama local model server as a systemd service |
| [`vs-code`](#vs-code) | `git`, `dotnet`, `ollama` | VS Code with C#, Git Graph, .NET Install Tool and Cline |
| [`gemma`](#gemma) | `ollama` | Gemma 4 26B-A4B model for Ollama and Cline |
| [`open-code`](#open-code) | `git` | OpenCode AI coding agent |
| [`xunit`](#xunit) | `dotnet` | xunit v3, Moq, coverlet.collector NuGet packages |
| [`avalonia`](#avalonia) | `dotnet` | Avalonia, CommunityToolkit.Mvvm, Tmds.DBus.Protocol and more NuGet packages, `dotnet new` templates |
| [`protobuf`](#protobuf) | `dotnet` | protobuf-net NuGet package |
| [`sqlite`](#sqlite) | `dotnet` | SQLite NuGet packages, DB Browser for SQLite |
| [`godot`](#godot) | `dotnet` | Godot game engine (.NET edition) |
| [`podman`](#podman) | none | Podman container engine |
| [`gimp`](#gimp) | none | GIMP photo editor |
| [`krita`](#krita) | none | Krita painting program |
| [`blender`](#blender) | none | Blender 3D modeling, animation and rendering |
| [`auto-remote`](#auto-remote) | `git`, `dotnet`, `ollama`, `gemma`, `open-code` | Auto Remote server as a systemd user service and its Linux desktop client with an app finder entry |

A bundle needs another only when its content or its setup uses it: the NuGet bundles and `godot` add to the feed of `dotnet`; `vs-code` configures the .NET SDK, git and Ollama; `gemma` is a model for `ollama`; `open-code` runs git; `auto-remote` runs git, scripts with `dotnet` and workspaces on the local model through `open-code` and `ollama`. `gemma` does not need `vs-code` (its Cline settings are only files), and nothing needs `a2000` (Ollama uses a GPU driver when there is one and runs on the CPU without).

`install <bundle>` installs the bundles it needs first if they are not installed yet (their files must be in the Bundles folder), and a bundle cannot be uninstalled while another installed bundle needs it.

## git

The latest stable git, from the official git-core PPA built for Ubuntu 24.04. Replaces an older git from the Ubuntu archive.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| git | 2.56.0 | Source control. Replaces an older git from the Ubuntu archive. | GPL-2.0, LGPL-2.1, MIT, ISC | Free use. |
| Ubuntu libraries | - | Runtime libraries git needs (curl, perl, expat, ...). | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## a2000

The NVIDIA Linux driver for the RTX A2000. The series is the newest one Ubuntu 24.04 packages, at most the series NVIDIA lists as its latest stable release (`latest.txt`: 620 at the last build, while Ubuntu packages 615), installed from the packages Ubuntu builds from it. The kernel modules are prebuilt and signed by Canonical, so they load with Secure Boot on and need no compiler. They are built for one kernel, the 24.04 base kernel (6.8.0-149), so the bundle brings that kernel as well. After the install, restart; the install says which kernel the modules are for, and a computer that starts a newer one (an HWE kernel, for example) has to be told to start that one in the boot menu. Ubuntu's build is behind NVIDIA's own latest number; the build says so.

The install reports the NVIDIA GPU it finds and warns if there is none or if it is not an RTX A2000 (it installs anyway). A newer driver that is already installed is kept. An older driver of another series (for example a 580 driver that Ubuntu installed, while the bundle brings 615) cannot be installed next to the bundle's, so its packages are removed first, logged, and not brought back by an uninstall; the same series is simply upgraded. The driver itself could not be tried on a real A2000 (see `Docs/Usage.md`).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| NVIDIA driver | 615.71.09 | Userspace driver for the display and for compute (`nvidia-driver-615-open`, libnvidia-*, `nvidia-utils` with `nvidia-smi`, firmware). | NVIDIA-Driver-License | Only on infrastructure the organization owns or leases, and only with NVIDIA hardware (an RTX A2000 qualifies). Binaries must stay unmodified. Must not be distributed outside the organization. Section 16 requires notices when it is delivered to the U.S. Government. |
| Open kernel modules | 615 | Prebuilt GPU kernel modules for Ubuntu's kernel (`linux-modules-nvidia-615-open`), signed by Canonical Ltd. Kernel Module Signing. | MIT, GPL-2.0 | Free use. |
| Linux kernel | 6.8.0-149 | The kernel the modules were built for (`linux-image`, `linux-modules`). Installed next to the current kernel, which stays bootable. | GPL-2.0 | Free use. |

## dotnet

The .NET 10 and .NET 11 SDKs side by side in `/usr/share/dotnet`, and the offline NuGet feed, set up so that C# builds without internet whichever SDK and target framework a project uses: NuGet restore uses only the offline feed (machine-wide and for every user), and .NET telemetry is off. `dotnet` runs the newest SDK (11); a `global.json` picks the 10 one. Both SDKs build net10.0 and net11.0 projects offline: the runtimes and targeting packs of both are in the same folder, and the feed holds the packages of a restore for net10.0 with the 10 SDK and of one for net11.0 with the 11 SDK, so packages that follow the .NET release (`Microsoft.Extensions.*`, the runtime packs, EF Core and so on) are there in their 10.x and their 11.x versions. A build takes the newest SDK of each of the two channels (10.0 is the long term support release, supported until 2028); other channels are not brought. The install adds what is missing next to what is already in the folder (an older patch version of a channel stays beside the new one, and the muxer is replaced only by one from a newer host), and an uninstall removes only what the install added.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| .NET 10 SDK | 10.0.404 | Builds and runs C# projects for net10.0. Includes runtime 10.0.15 and ASP.NET Core. | MIT | Free use. |
| .NET 11 SDK | 11.0.100 | Builds and runs C# projects for net11.0 and net10.0. Includes runtime 11.0.0 and ASP.NET Core. | MIT | Free use. |
| Offline NuGet feed | net10.0, net11.0 | Basic .NET packages with their dependencies in `/opt/bundler/nuget-feed`: Microsoft.Extensions.*, System.CommandLine, System.IO.Ports, System.Reactive, System.Linq.Async, System.Drawing.Common, System.Text.Json, Microsoft.NET.Test.Sdk, the runtime, apphost and ILLink packs for linux-x64 and win-x64, and what the `dotnet new` console, classlib, worker, webapi, web, mvc, razor, blazor and grpc templates reference (list in `App/src/Resources/dotnet/packages.txt`). | MIT, Apache-2.0, BSD-3-Clause | Free use. |
| Support libraries | - | What .NET needs to run (ICU, OpenSSL, tzdata, ca-certificates, ...); installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## ollama

Ollama as a local-only systemd service on `127.0.0.1:11434` (no cloud features, one model at a time, kept loaded for 30 minutes). It runs the models of other bundles, such as `gemma`. A computer with an NVIDIA GPU makes the service wait for the driver before it starts.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Ollama | 0.40.16 | Local model server, a systemd service on `127.0.0.1:11434`. The tarball bundles NVIDIA CUDA runtime libraries. | MIT | The NVIDIA CUDA libraries in its tarball may only be used through Ollama, under the government restricted-rights terms of NVIDIA's CUDA license. |
| Support libraries | - | zstd, to unpack the Ollama archive; installed only when missing. | BSD-3-Clause, GPL-2.0 | Free use. |

## vs-code

VS Code for every user, set up to work offline: it finds the .NET SDK and git, telemetry and update checks are off, workspace trust is off so the C# language server starts without a prompt, Cline talks to the local Ollama and has rules for working offline, and git defaults to branch `main` with VS Code as its editor. Needs `git`, `dotnet` and `ollama`.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Visual Studio Code | 1.145.3 | Code editor. | VSCode-License | Only to develop and test the organization's own applications, including inside its internal network. Must not be shared, published or offered to others. |
| C# extension | 2.160.4 | C# language support in VS Code, with its Roslyn language server and debugger. The C# Dev Kit is left out on purpose, see below. | CSharp-Extension-License | Only with VS Code or Visual Studio, to develop and test the organization's own applications. Must not be shared outside the organization. The bundled debugger has its own Microsoft terms of the same kind. |
| .NET Install Tool | 3.2.0 | Lets the C# extension find the installed .NET (`ms-dotnettools.vscode-dotnet-runtime`). | MIT | Free use. |
| Git Graph | 1.30.0 | Commit graph view in VS Code (`mhutchie.git-graph`). | GitGraph-License | MIT with an added sentence: "Permission is NOT GRANTED to publish, distribute, sublicense, and/or sell derivative works". Use, copy, modify and merge are allowed. Do not distribute modified copies outside the organization. |
| Cline | 4.1.26 | Coding agent for VS Code that works through the local Ollama (`saoudrizwan.claude-dev`). Its cloud sign-in and hosted models are not used. | Apache-2.0 | Free use. |
| Support libraries | - | xdg-utils and ca-certificates for VS Code; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## gemma

Gemma 4 26B-A4B for Ollama, set as the model of Cline (32K context; the Cline settings are written whether or not VS Code is installed). Needs roughly 24 GB of RAM or more. An identical model that is already installed is skipped; a changed model of the same name replaces the old one.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Gemma 4 26B-A4B | gemma4:26b | Large language model (25.2B parameters, Q4_K_M, 18.7 GB, needs Ollama 0.30.9 or newer). | Apache-2.0 | Free use. |

## open-code

OpenCode, the AI coding agent for the command line, which runs the models of the local Ollama (such as `gemma`). It comes from the releases of `anomalyco/opencode` (the baseline x64 build, which runs on every x86-64 CPU), checked against the SHA-256 checksum GitHub publishes. Auto Remote 1.2.0 drives OpenCode 2.x, so the newest stable 2.x release is taken (the repository's "latest" release is a 1.x one). It is installed in `/opt/open-code` with an `opencode` command in `/usr/local/bin`; an OpenCode that is the same or newer is kept. Needs `git`, which OpenCode runs for project detection and file status.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| OpenCode | 2.2.5 | AI coding agent (a single self-contained program). | MIT | Free use. |


## xunit

xunit v3 only (no xunit 2.x), with Moq and coverlet.collector. Create projects with `dotnet new xunit3 -f net10.0` (the template defaults to net8.0 and offers frameworks up to net10.0; for net11.0 change `TargetFramework` in the project afterwards, which builds offline).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| xunit v3 | 4.0.3 | Test framework (xunit.v3, .core, .assert, .common, .extensibility.core, .runner.*, .mtp-*) and the `xunit3` templates. | Apache-2.0 | Free use. |
| xunit.runner.visualstudio | 4.0.1 | VSTest adapter used by xunit v3 projects, with xunit.analyzers. | Apache-2.0 | Free use. |
| xunit.abstractions | 2.0.3 | Interfaces that xunit.v3.runner.utility depends on. | Apache-2.0 | Free use. |
| Microsoft.NET.Test.Sdk | 18.11.1 | Test host. | MIT | Free use. |
| coverlet.collector | 10.1.0 | Code coverage collector. | MIT | Free use. |
| Moq | 4.21.0 | Mocking library. | BSD-3-Clause | Free use. |

## avalonia

Avalonia 12 packages for desktop targets (browser, Android and iOS need workloads that cannot be installed offline), the packages commonly used with them, and the `dotnet new` templates (`avalonia.app`, `avalonia.mvvm`, `avalonia.xplat` and the item templates).

The stock templates make every project reference `AvaloniaUI.DiagnosticsSupport`, the bridge to AvaloniaUI's paid Developer Tools, a package with no stated license, and call `.WithDeveloperTools()` from `Program.cs`. The template pack (MIT) is therefore installed with those references and calls removed (`TemplatePatcher`, done at build time); the pack is marked as modified in its description, and the generated projects build offline.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Avalonia | 12.1.6 | Cross-platform UI framework: Avalonia, .Desktop, .Themes.Fluent, .Themes.Simple, .Fonts.Inter, .Controls.DataGrid, .Controls.ColorPicker, .Skia, .X11, .Win32, .Native, .FreeDesktop, .Markup.Xaml.Loader, .Remote.Protocol, .Headless, .Headless.XUnit. | MIT | Free use. |
| Avalonia.Templates | 12.1.6 | `dotnet new` templates, modified as described above. | MIT | Free use. |
| Avalonia.Angle.Windows.Natives | - | ANGLE graphics library for Windows. | BSD-3-Clause | Free use. |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM helpers. | MIT | Free use. |
| Tmds.DBus.Protocol | 0.95.2 | D-Bus protocol library. | MIT | Free use. |
| Avalonia add-ons | - | Text editor control, behaviors, icons, themes and dialogs (AvaloniaEdit, Xaml.Behaviors.Avalonia, Projektanker.Icons.Avalonia, Material.Icons.Avalonia, FluentAvaloniaUI, Semi.Avalonia, DialogHost.Avalonia). | MIT | Free use. |

## protobuf

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| protobuf-net | 3.4.40 | Protocol Buffers serialization for .NET, with protobuf-net.Core. | Apache-2.0 | Free use. |

## sqlite

Install the EF tool with `dotnet tool install --global dotnet-ef`; it comes from the offline feed.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Microsoft.Data.Sqlite and EF Core SQLite | 10.0.15, 11.0.0 | ADO.NET provider, EF Core provider (Microsoft.EntityFrameworkCore.Sqlite and .Design) and the EF tool package dotnet-ef. | MIT | Free use. |
| SQLitePCLRaw | 3.0.5 | Native SQLite for the .NET providers: the wrappers and the native SQLite builds. | MIT, Public-Domain | Free use. |
| System.Data.SQLite | 2.0.4 | Another SQLite provider, with System.Data.SQLite.Core 1.0.119. | Public-Domain | Free use. |
| sqlite-net-pcl | 1.11.285 | SQLite wrapper. | MIT | Free use. |
| DB Browser for SQLite | 3.12.2 | Graphical SQLite database tool (the Ubuntu 24.04 package). | GPL-3.0, MPL-2.0 | Free use. |

## godot

The Godot .NET edition (C# support) with the matching export templates, which are linked into each user's Godot folder. Creates the `godot` command and a menu entry. A Godot version that is already there is detected by asking it for its version.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Godot | 4.7.2 | Game engine and editor, the .NET edition for linux x86_64, with its export templates. | MIT | Free use. |
| Godot NuGet packages | 4.7.2 | Godot.NET.Sdk, GodotSharp, GodotSharpEditor and Godot.SourceGenerators, added to the offline feed. | MIT | Free use. |
| Editor libraries | - | Needed by the editor (X11, GL, audio, ..., unzip); installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## podman

The Podman container engine with what rootless containers need. The version is the one in the Ubuntu 24.04 archive (4.9.3): upstream is at 6.x, but for Linux it only publishes `podman-remote`, a client that needs a Podman service somewhere else, so the newest complete engine packaged for 24.04 is used. Accounts without a range of subordinate user and group ids get one (recorded, and taken away again on uninstall). No container images are included: `podman load` needs images from files. Images and volumes stay on uninstall.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Podman | 4.9.3 | Container engine (`podman`, `golang-github-containers-common`). | Apache-2.0 | Free use. |
| conmon, netavark, aardvark-dns | 2.1.10, 1.4.0, 1.4.0 | Container monitor, network stack and DNS. | Apache-2.0 | Free use. |
| crun | 1.14.1 | OCI runtime. | GPL-2.0, LGPL-2.1 | Free use. |
| passt | 2024-02-20 | Rootless networking. | GPL-2.0, BSD-3-Clause | Free use. |
| Rootless support | - | uidmap, slirp4netns, catatonit and dbus-user-session; installed only when missing. | GPL-2.0, MIT | Free use. |

## gimp

GIMP (2.10) from the Ubuntu 24.04 archive, with the libraries it needs (GEGL, babl, poppler and others). GIMP is for retouching photos, compositing and graphics. The Debian copyright files of the program and its data package were searched for non-commercial, research-only or similar terms and have none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| GIMP | 2.10.36 | Photo editor and graphics program (`gimp` and the libgimp libraries). | GPL-3.0, LGPL-3.0, LGPL-2.1, MIT, BSD-2-Clause | Free use. |
| GIMP artwork | 2.10.36 | Brushes, patterns, icons and other artwork shipped with GIMP (`gimp-data`). | GPL-3.0, CC-BY-SA-4.0, CC-BY-SA-3.0, CC0-1.0 | Free use. |
| Libraries | - | GEGL, babl, poppler and the other libraries GIMP needs; installed only when missing. | GPL-2.0, GPL-3.0, LGPL-2.1, LGPL-3.0, MIT, BSD-3-Clause | Free use. The license differs per package. |

## krita

Krita (5.2) from the Ubuntu 24.04 archive, with the libraries it needs (Qt 5, KDE Frameworks and others). Krita is for painting, drawing and textures. The Debian copyright files of the program and its data package were searched for non-commercial, research-only or similar terms and have none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Krita | 5.2.2 | Painting and drawing program (`krita`). | GPL-3.0, GPL-2.0, LGPL-2.1, LGPL-3.0, MIT, BSD-3-Clause | Free use. |
| Krita brushes and bundled code | 5.2.2 | Brushes, presets, patterns and small third-party libraries shipped with Krita (`krita-data`). | CC-BY-SA-3.0, CC0-1.0, BSL-1.0, NCSA, Unlicense | Free use. |
| Libraries | - | Qt 5 and the other libraries Krita needs; installed only when missing. | GPL-2.0, GPL-3.0, LGPL-2.1, LGPL-3.0, MIT, BSD-3-Clause | Free use. The license differs per package. |

## blender

Blender, the 3D modeling, sculpting, animation and rendering program, which exports glTF, FBX, OBJ and STL for games and apps (such as Godot). It comes from Blender's own Linux release archive, because the Ubuntu 24.04 archive is far behind (4.0.2 against 5.3.0 at the last build); the archive is checked against the SHA-256 checksum Blender publishes. It is installed in `/opt/blender` with a `blender` command and a menu entry, and an install of a newer Blender replaces the older one. The archive carries its own license folder, which lists every library built into Blender; it was searched for non-commercial, research-only or similar terms and has none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Blender | 5.3.0 | 3D modeling, sculpting, animation and rendering program. | GPL-3.0 | Free use. |
| Libraries built into Blender | 5.3.0 | Image, video, geometry, physics and scripting libraries. | Apache-2.0, BSD-2-Clause, BSD-3-Clause, BSL-1.0, GPL-2.0, LGPL-2.1, LGPL-3.0, MIT, MPL-2.0, NCSA, Python-2.0, Zlib | Free use. |
| Smaller library licenses | 5.3.0 | Licenses of smaller libraries and code in Blender (compression, PNG and TIFF, FreeType, OpenGL headers and others). | 0BSD, bzip2-1.0.6, FTL, libpng-2.0, libtiff, MIT-Khronos-old, SGI-B-2.0, TOST-1.0 | Free use. |
| Fonts | 5.3.0 | The fonts of Blender's interface and text objects. | OFL-1.1, Bitstream-Vera, Arev-Fonts | Free use. |
| Support libraries | - | X11, OpenGL, Wayland and other system libraries the program needs, and xz to unpack it; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. The license differs per package. |

## auto-remote

Auto Remote 1.2.0 (`Markwardt-Labs/Auto-Remote`): a server that hosts AI coding workspaces on this computer, and a desktop client for using them. Unlike every other bundle this one is pinned to one release on purpose (1.2.0), not to the latest. Installed the way the author runs it: the server and the client are in `/opt/auto-remote`, the server is a systemd user service of every normal account (enabled and lingering, so it runs without a login), and the client has an app finder entry with its icon. A server's config, `~/.config/auto-remote/config.json`, is created with a new random key when it is missing (read the key there to connect a client) and is never overwritten; with several accounts the second one's server gets the next port after the default 52679, and so on. An uninstall removes the services, the programs and the entry, and keeps each account's config and its data in `~/.local/share/AutoRemote`. Needs `git` (for the server's git features), `dotnet` (the server runs scripts with `dotnet`), `ollama`, `gemma` and `open-code`, so that a workspace set to OpenCode runs the local Gemma model without any network: the server's default `localModelUrl` is the Ollama at `http://localhost:11434/v1` and `opencode` is on the server's path. Claude Code and Codex (`claude`, `codex`) are not part of this bundle. The server downloads its voice input model (Whisper `small.en`, about 470 MB) from the internet in the background when it starts; without a network it runs fine (checked offline in a chroot) but has no voice input, until a `ggml` model file is named in `speechModel` in the config.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Auto Remote server | 1.2.0 | Command line server for the Android, desktop and browser clients, self-contained (.NET runtime, Live AI, Whisper.net, whisper.cpp and ggml inside). A systemd user service for every normal account. | MIT | Free use. |
| Auto Remote desktop client | 1.2.0 | Avalonia desktop app, self-contained, with an app finder entry and its icon. | MIT, BSD-2-Clause, BSD-3-Clause | Free use. |
| Support libraries | - | ICU and OpenSSL for the server, and X11, OpenGL, Vulkan, fontconfig (the install builds its font cache, without which the client aborts on its first start) and fonts for the desktop client; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause, Apache-2.0 | Free use. The license differs per package. |

## License review

Where a license text or a metadata field was read for a service, and where it was not read in full. Services that are not listed take their license from the package metadata (SPDX expression in a `.nuspec`, the repository's license or the Debian copyright file).

| Bundle | Service | Checked from |
|---|---|---|
| git | git | Debian copyright file (GPL-2.0, with LGPL-2.1+, Expat and ISC parts) |
| a2000 | NVIDIA driver | `licenses/NVIDIA-Driver-License.txt`; the 615 package carries the same license text (compared with the one read for the 595 package: nothing in the text on file is missing from it) |
| dotnet | .NET 10 and 11 SDKs | `LICENSE.txt` in the tarball |
| vs-code | Visual Studio Code | `licenses/VSCode-License.rtf` |
| vs-code | C# extension | `licenses/CSharp-Extension-License.txt` |
| vs-code | .NET Install Tool | license text |
| vs-code | Git Graph | `licenses/GitGraph-LICENSE.txt` |
| vs-code | Cline | `licenses/Cline-LICENSE.txt` |
| ollama | Ollama | upstream repository license (MIT); not read in full: the tarball's third-party notices for llama.cpp, ggml and others, and the CUDA terms (from the previous project's review, not re-read) |
| dotnet | Offline NuGet feed | nuspec SPDX expressions, checked automatically at build time |
| gemma | Gemma 4 26B-A4B | the license text the Ollama registry ships with the model, `licenses/Gemma4-26B-license.txt`; Google is a US organization |
| xunit | xunit.abstractions | the license URL, `licenses/reviewed-nuget.tsv` (.NET Foundation) |
| avalonia | Avalonia.Templates | the repository's LICENSE at the packaged commit; the package itself declares no license; the template modification is allowed by the MIT license |
| avalonia | Avalonia.Angle.Windows.Natives | LICENSE inside the package |
| sqlite | SQLitePCLRaw | LICENSE.txt in the packages (MIT for the wrappers; SQLite is public domain) |
| sqlite | System.Data.SQLite | SQLite copyright page and the nuspec copyright field |
| sqlite | sqlite-net-pcl | license text |
| sqlite | DB Browser for SQLite | Debian copyright file (GPL-3.0-or-later and MPL-2.0) |
| godot | Godot | upstream repository license; games exported with it carry Godot's license notices |
| podman | Podman | Debian copyright file of podman 4.9.3 |
| podman | conmon, netavark, aardvark-dns | Debian copyright files |
| podman | crun | Debian copyright file (GPL-2.0+ and LGPL-2.1+) |
| podman | passt | Debian copyright file (GPL-2.0+ and BSD-3-Clause) |
| gimp | GIMP | Debian copyright files of gimp and gimp-data 2.10.36 (GPL-3+, GPL-2+, LGPL-3+, MIT, Expat and BSD-2-clause code; artwork under CC-BY-SA-3.0, CC-BY-SA-4.0 and CC0-1.0); searched for non-commercial or similar terms, none found |
| krita | Krita | Debian copyright files of krita and krita-data 5.2.2 (GPL, LGPL, BSD, BSL-1.0, NCSA, Unlicense, CC-BY-SA-3.0, CC0-1.0, public domain); searched for non-commercial or similar terms, none found (the only hit is the Unlicense text itself, which allows commercial use) |
| blender | Blender | the `license` folder inside the official release archive (`license.md`, `licenses.json`): Blender is GPL-3.0-or-later and the libraries inside it with their licenses are listed there; searched for non-commercial or similar terms, none found |
| open-code | OpenCode | the repository `LICENSE` at the release tag (MIT) and the npm package metadata (MIT); not read: the licenses of the JavaScript dependencies compiled into the program, since the release carries no third-party notices file |
| auto-remote | Auto Remote server and desktop client | the repository `LICENSE` (MIT) and the release's `THIRD-PARTY-NOTICES.txt` (both read; the notices list the .NET runtime, Live AI, Whisper.net, whisper.cpp and ggml, all MIT); the Whisper.net package's `LICENSE` read (MIT); the client's Avalonia (MIT), SkiaSharp and Skia (MIT, BSD-3-Clause) and Markdig (BSD-2-Clause) from their package metadata, not read in full, and the release's notices file does not cover the client; the voice model the server downloads at run time is not part of the bundle and was not reviewed |

## Left out on purpose

| Name | Why |
|---|---|
| C# Dev Kit (ms-dotnettools.csdevkit) | Free only for individuals, academic and open-source work, and commercial teams up to 5 developers. |
| AvaloniaUI.DiagnosticsSupport, Avalonia.Controls.TreeDataGrid, Avalonia Developer Tools | AvaloniaUI paid products or packages with no stated license. |
| Avalonia.ReactiveUI, Avalonia.Diagnostics | Only exist for Avalonia 11, not compatible with the 12.x packages. |
| FluentAssertions 8 and later | Commercial license. Not requested, not included. |
| Docker Engine | Replaced by Podman. |

## If the scope changes

If any bundle ever leaves the organization, redo the license review first. At minimum: Visual Studio Code and the C# extension may not be shared, Git Graph's derivative-work ban applies, the GPL source offer applies to git, crun, GIMP, Krita and others, the CC-BY-SA artwork in GIMP and Krita needs attribution and share-alike when it is passed on, Ubuntu's trademark rules apply to Ubuntu packages, the NVIDIA driver may only be redistributed under its own section 1.1d (unmodified binaries and the license text with each copy), and NVIDIA's redistribution terms apply to the CUDA libraries inside Ollama.
