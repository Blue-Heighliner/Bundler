# Bundles

Every bundle Bundler can build and install. Each has a Target, the system it is made for; all of them target Ubuntu 26.04 and the app does not check the system it installs on. A bundle is built on an online computer into one file, `<name>.bundle`, in the `bundles` folder next to the app; that file is copied into the `bundles` folder next to the app on the offline Ubuntu 26.04 computer. A build downloads the latest stable release of each component, so a file always holds the versions that were latest when it was built. Bundles have no version numbers of their own, only the versions of the services inside them (the Version column below, which are the ones of the files built on 2026-10-29 and 2026-10-30). Whether a file is newer than what is installed is for the person to judge, from when the file was last changed; moving to it means reinstalling the bundle, which uninstalls it and installs the new file.

Scope: internal use inside one organization, on machines it owns, nothing redistributed (see `AGENTS.md`). **License** lists the licenses by short name; in the app, clicking one opens its full text (`licenses/texts/`). The text of a standard license is the standard text, the copyright line of each project is in its package. **Usage** says "Free use" when the licenses put no restriction on that scope, otherwise it lists the restrictions. "License review" below says where the licenses were read. This is not legal advice.

How installs treat what is already on the computer: a missing component is installed, an older one is replaced, the same version is skipped, and a newer one is kept (it is never replaced by an older one). NuGet packages are only ever added: older versions stay in the feed. An uninstall removes only what the bundle itself installed or replaced.

| Bundle | Needs | Installs |
|---|---|---|
| [`git`](#git) | none | git |
| [`a2000`](#a2000) | none | NVIDIA driver for the RTX A2000 |
| [`dev`](#dev) | `git` | .NET SDK, Ollama, VS Code with C#, Git Graph, .NET Install Tool and Cline, offline NuGet feed |
| [`gemma`](#gemma) | `dev` | Gemma 4 26B-A4B model for Ollama, Cline and VS Code |
| [`xunit`](#xunit) | `dev` | xunit v3, Moq, coverlet.collector NuGet packages |
| [`avalonia`](#avalonia) | `dev` | Avalonia, CommunityToolkit.Mvvm, Tmds.DBus.Protocol and more NuGet packages, `dotnet new` templates |
| [`protobuf`](#protobuf) | `dev` | protobuf-net NuGet package |
| [`sqlite`](#sqlite) | `dev` | SQLite NuGet packages, DB Browser for SQLite |
| [`godot`](#godot) | `dev` | Godot game engine (.NET edition) |
| [`podman`](#podman) | none | Podman container engine |
| [`2d`](#2d) | none | GIMP photo editor, Krita painting program |
| [`3d`](#3d) | none | Blender 3D modeling, animation and rendering |

`install <bundle>` installs the bundles it needs first if they are not installed yet (their files must be in the bundles folder), and a bundle cannot be uninstalled while another installed bundle needs it.

## git

The latest stable git, from the official git-core PPA built for Ubuntu 26.04. Replaces an older git from the Ubuntu archive.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| git | 2.56.0 | Source control. Replaces an older git from the Ubuntu archive. | GPL-2.0, LGPL-2.1, MIT, ISC | Free use. |
| Ubuntu libraries | - | Runtime libraries git needs (curl, perl, expat, ...). | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## a2000

The NVIDIA Linux driver for the RTX A2000. The series is the one NVIDIA lists as its latest stable release (`latest.txt`, currently 595), installed from the packages Ubuntu builds from it. The kernel modules are prebuilt and signed by Canonical, so they load with Secure Boot on and need no compiler. They are built for a specific kernel, so the bundle brings that kernel (7.0.0-42) as well: after the install, restart, and the computer boots into it. Ubuntu's build can be a little behind NVIDIA's own latest number (595.99.02 against 595.104.02 at the last build); the build says so.

The install reports the NVIDIA GPU it finds and warns if there is none or if it is not an RTX A2000 (it installs anyway). A newer driver that is already installed is kept. The driver itself could not be tried on a real A2000 (see `Docs/Usage.md`).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| NVIDIA driver | 595.99.02 | Userspace driver for the display and for compute (`nvidia-driver-595-open`, libnvidia-*, `nvidia-utils` with `nvidia-smi`, firmware). | NVIDIA-Driver-License | Only on infrastructure the organization owns or leases, and only with NVIDIA hardware (an RTX A2000 qualifies). Binaries must stay unmodified. Must not be distributed outside the organization. Section 16 requires notices when it is delivered to the U.S. Government. |
| Open kernel modules | 595 | Prebuilt GPU kernel modules for Ubuntu's kernel (`linux-modules-nvidia-595-open`), signed by Canonical Ltd. Kernel Module Signing. | MIT, GPL-2.0 | Free use. |
| Linux kernel | 7.0.0-42 | The kernel the modules were built for (`linux-image`, `linux-modules`). Installed next to the current kernel, which stays bootable. | GPL-2.0 | Free use. |

## dev

Everything for C# development without internet, set up to work together: NuGet restore uses only the offline feed (machine-wide and for every user), VS Code finds the .NET SDK and git, telemetry and update checks are off, Cline talks to the local Ollama and has rules for working offline, git defaults to branch `main` with VS Code as its editor, and workspace trust is off so the C# language server starts without a prompt.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| .NET SDK | 10.0.403 | Builds and runs C# projects. Includes runtime 10.0.14 and ASP.NET Core. | MIT | Free use. |
| Visual Studio Code | 1.144.0 | Code editor. | VSCode-License | Only to develop and test the organization's own applications, including inside its internal network. Must not be shared, published or offered to others. |
| C# extension | 2.160.4 | C# language support in VS Code, with its Roslyn language server and debugger. The C# Dev Kit is left out on purpose, see below. | CSharp-Extension-License | Only with VS Code or Visual Studio, to develop and test the organization's own applications. Must not be shared outside the organization. The bundled debugger has its own Microsoft terms of the same kind. |
| .NET Install Tool | 3.2.0 | Lets the C# extension find the installed .NET (`ms-dotnettools.vscode-dotnet-runtime`). | MIT | Free use. |
| Git Graph | 1.30.0 | Commit graph view in VS Code (`mhutchie.git-graph`). | GitGraph-License | MIT with an added sentence: "Permission is NOT GRANTED to publish, distribute, sublicense, and/or sell derivative works". Use, copy, modify and merge are allowed. Do not distribute modified copies outside the organization. |
| Cline | 4.1.26 | Coding agent for VS Code that works through the local Ollama (`saoudrizwan.claude-dev`). Its cloud sign-in and hosted models are not used. | Apache-2.0 | Free use. |
| Ollama | 0.40.12 | Local model server, a systemd service on `127.0.0.1:11434`. The tarball bundles NVIDIA CUDA runtime libraries. | MIT | The NVIDIA CUDA libraries in its tarball may only be used through Ollama, under the government restricted-rights terms of NVIDIA's CUDA license. |
| Support libraries | - | Needed to unpack and run VS Code, .NET and Ollama (zstd, ICU, OpenSSL, tzdata, ca-certificates, ...); installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |
| Offline NuGet feed | net10.0 | Basic .NET packages with their dependencies in `/opt/bundler/nuget-feed`: Microsoft.Extensions.*, System.CommandLine, System.IO.Ports, System.Reactive, System.Linq.Async, System.Drawing.Common, System.Text.Json, Microsoft.NET.Test.Sdk, the runtime, apphost and ILLink packs for linux-x64 and win-x64, and what the `dotnet new` console, classlib, worker, webapi, web, mvc, razor, blazor and grpc templates reference (list in `App/src/Resources/dev/packages.txt`). | MIT, Apache-2.0, BSD-3-Clause | Free use. |

## gemma

Gemma 4 26B-A4B for Ollama, set as the model of Cline in VS Code (32K context). Needs roughly 24 GB of RAM or more. An identical model that is already installed is skipped; a changed model of the same name replaces the old one.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Gemma 4 26B-A4B | gemma4:26b | Large language model (25.2B parameters, Q4_K_M, 18.7 GB, needs Ollama 0.30.9 or newer). | Apache-2.0 | Free use. |

## xunit

xunit v3 only (no xunit 2.x), with Moq and coverlet.collector. Create projects with `dotnet new xunit3 -f net10.0` (the template defaults to net8.0, and only the .NET 10 runtime is installed).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| xunit v3 | 4.0.2 | Test framework (xunit.v3, .core, .assert, .common, .extensibility.core, .runner.*, .mtp-*) and the `xunit3` templates. | Apache-2.0 | Free use. |
| xunit.runner.visualstudio | 4.0.1 | VSTest adapter used by xunit v3 projects, with xunit.analyzers. | Apache-2.0 | Free use. |
| xunit.abstractions | 2.0.3 | Interfaces that xunit.v3.runner.utility depends on. | Apache-2.0 | Free use. |
| Microsoft.NET.Test.Sdk | 18.11.0 | Test host. | MIT | Free use. |
| coverlet.collector | 10.1.0 | Code coverage collector. | MIT | Free use. |
| Moq | 4.21.0 | Mocking library. | BSD-3-Clause | Free use. |

## avalonia

Avalonia 12 packages for desktop targets (browser, Android and iOS need workloads that cannot be installed offline), the packages commonly used with them, and the `dotnet new` templates (`avalonia.app`, `avalonia.mvvm`, `avalonia.xplat` and the item templates).

The stock templates make every project reference `AvaloniaUI.DiagnosticsSupport`, the bridge to AvaloniaUI's paid Developer Tools, a package with no stated license, and call `.WithDeveloperTools()` from `Program.cs`. The template pack (MIT) is therefore installed with those references and calls removed (`TemplatePatcher`, done at build time); the pack is marked as modified in its description, and the generated projects build offline.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Avalonia | 12.1.4 | Cross-platform UI framework: Avalonia, .Desktop, .Themes.Fluent, .Themes.Simple, .Fonts.Inter, .Controls.DataGrid, .Controls.ColorPicker, .Skia, .X11, .Win32, .Native, .FreeDesktop, .Markup.Xaml.Loader, .Remote.Protocol, .Headless, .Headless.XUnit. | MIT | Free use. |
| Avalonia.Templates | 12.1.4 | `dotnet new` templates, modified as described above. | MIT | Free use. |
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
| Microsoft.Data.Sqlite and EF Core SQLite | 10.0.14 | ADO.NET provider, EF Core provider (Microsoft.EntityFrameworkCore.Sqlite and .Design) and the EF tool package dotnet-ef. | MIT | Free use. |
| SQLitePCLRaw | 3.0.5 | Native SQLite for the .NET providers: the wrappers and the native SQLite builds. | MIT, Public-Domain | Free use. |
| System.Data.SQLite | 2.0.4 | Another SQLite provider, with System.Data.SQLite.Core 1.0.119. | Public-Domain | Free use. |
| sqlite-net-pcl | 1.11.285 | SQLite wrapper. | MIT | Free use. |
| DB Browser for SQLite | 3.13.1 | Graphical SQLite database tool (the Ubuntu 26.04 package, which is the latest stable upstream release). | GPL-3.0, MPL-2.0 | Free use. |

## godot

The Godot .NET edition (C# support) with the matching export templates, which are linked into each user's Godot folder. Creates the `godot` command and a menu entry. A Godot version that is already there is detected by asking it for its version.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Godot | 4.7.2 | Game engine and editor, the .NET edition for linux x86_64, with its export templates. | MIT | Free use. |
| Godot NuGet packages | 4.7.2 | Godot.NET.Sdk, GodotSharp, GodotSharpEditor and Godot.SourceGenerators, added to the offline feed. | MIT | Free use. |
| Editor libraries | - | Needed by the editor (X11, GL, audio, ..., unzip); installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## podman

The Podman container engine with what rootless containers need. The version is the one in the Ubuntu 26.04 archive: upstream is at 6.x, but for Linux it only publishes `podman-remote`, a client that needs a Podman service somewhere else, so the newest complete engine packaged for 26.04 is used. Accounts without a range of subordinate user and group ids get one (recorded, and taken away again on uninstall). No container images are included: `podman load` needs images from files. Images and volumes stay on uninstall.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Podman | 5.7.0 | Container engine (`podman`, `golang-github-containers-common`). | Apache-2.0 | Free use. |
| conmon, netavark, aardvark-dns | 2.1.13, 1.16.1, 1.16.0 | Container monitor, network stack and DNS. | Apache-2.0 | Free use. |
| crun | 1.21 | OCI runtime. | GPL-2.0, LGPL-2.1 | Free use. |
| passt | 2026-01-20 | Rootless networking. | GPL-2.0, BSD-3-Clause | Free use. |
| Rootless support | - | uidmap, slirp4netns, catatonit and dbus-user-session; installed only when missing. | GPL-2.0, MIT | Free use. |

## 2d

GIMP and Krita from the Ubuntu 26.04 archive, with the libraries they need (Qt 6, GEGL, babl, poppler, OpenColorIO and others). GIMP is for retouching photos, compositing and graphics; Krita is for painting, drawing and textures. The Debian copyright files of both programs and of their data packages were searched for non-commercial, research-only or similar terms and have none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| GIMP | 3.2.2 | Photo editor and graphics program (`gimp` and the libgimp libraries). | GPL-3.0, LGPL-3.0, LGPL-2.1, MIT, BSD-2-Clause | Free use. |
| GIMP artwork | 3.2.2 | Brushes, patterns, icons and other artwork shipped with GIMP (`gimp-data`). | GPL-3.0, CC-BY-SA-4.0, CC-BY-SA-3.0, CC0-1.0 | Free use. |
| Krita | 6.0.1 | Painting and drawing program (`krita`). | GPL-3.0, GPL-2.0, LGPL-2.1, LGPL-3.0, MIT, BSD-3-Clause | Free use. |
| Krita brushes and bundled code | 6.0.1 | Brushes, presets, patterns and small third-party libraries shipped with Krita (`krita-data`). | CC-BY-SA-3.0, CC0-1.0, BSL-1.0, NCSA, Unlicense | Free use. |
| Libraries | - | Qt 6, GEGL, babl, poppler, OpenColorIO and the other libraries both programs need; installed only when missing. | GPL-2.0, GPL-3.0, LGPL-2.1, LGPL-3.0, MIT, BSD-3-Clause | Free use. The license differs per package. |

## 3d

Blender, the 3D modeling, sculpting, animation and rendering program, which exports glTF, FBX, OBJ and STL for games and apps (such as Godot). It comes from Blender's own Linux release archive, because the Ubuntu 26.04 archive is a release or two behind (5.0.1 against 5.2.3 at the last build); the archive is checked against the SHA-256 checksum Blender publishes. It is installed in `/opt/blender` with a `blender` command and a menu entry, and an install of a newer Blender replaces the older one. The archive carries its own license folder, which lists every library built into Blender; it was searched for non-commercial, research-only or similar terms and has none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Blender | 5.2.3 | 3D modeling, sculpting, animation and rendering program. | GPL-3.0 | Free use. |
| Libraries built into Blender | 5.2.3 | Image, video, geometry, physics and scripting libraries. | Apache-2.0, BSD-2-Clause, BSD-3-Clause, BSL-1.0, GPL-2.0, LGPL-2.1, LGPL-3.0, MIT, MPL-2.0, NCSA, Python-2.0, Zlib | Free use. |
| Smaller library licenses | 5.2.3 | Licenses of smaller libraries and code in Blender (compression, PNG and TIFF, FreeType, OpenGL headers and others). | 0BSD, bzip2-1.0.6, FTL, libpng-2.0, libtiff, MIT-Khronos-old, SGI-B-2.0, TOST-1.0 | Free use. |
| Fonts | 5.2.3 | The fonts of Blender's interface and text objects. | OFL-1.1, Bitstream-Vera, Arev-Fonts | Free use. |
| Support libraries | - | X11, OpenGL, Wayland and other system libraries the program needs, and xz to unpack it; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. The license differs per package. |

## License review

Where a license text or a metadata field was read for a service, and where it was not read in full. Services that are not listed take their license from the package metadata (SPDX expression in a `.nuspec`, the repository's license or the Debian copyright file).

| Bundle | Service | Checked from |
|---|---|---|
| git | git | Debian copyright file (GPL-2.0, with LGPL-2.1+, Expat and ISC parts) |
| a2000 | NVIDIA driver | `licenses/NVIDIA-Driver-License.txt` |
| dev | .NET SDK | `LICENSE.txt` in the tarball |
| dev | Visual Studio Code | `licenses/VSCode-License.rtf` |
| dev | C# extension | `licenses/CSharp-Extension-License.txt` |
| dev | .NET Install Tool | license text |
| dev | Git Graph | `licenses/GitGraph-LICENSE.txt` |
| dev | Cline | `licenses/Cline-LICENSE.txt` |
| dev | Ollama | upstream repository license (MIT); not read in full: the tarball's third-party notices for llama.cpp, ggml and others, and the CUDA terms (from the previous project's review, not re-read) |
| dev | Offline NuGet feed | nuspec SPDX expressions, checked automatically at build time |
| gemma | Gemma 4 26B-A4B | the license text the Ollama registry ships with the model, `licenses/Gemma4-26B-license.txt`; Google is a US organization |
| xunit | xunit.abstractions | the license URL, `licenses/reviewed-nuget.tsv` (.NET Foundation) |
| avalonia | Avalonia.Templates | the repository's LICENSE at the packaged commit; the package itself declares no license; the template modification is allowed by the MIT license |
| avalonia | Avalonia.Angle.Windows.Natives | LICENSE inside the package |
| sqlite | SQLitePCLRaw | LICENSE.txt in the packages (MIT for the wrappers; SQLite is public domain) |
| sqlite | System.Data.SQLite | SQLite copyright page and the nuspec copyright field |
| sqlite | sqlite-net-pcl | license text |
| sqlite | DB Browser for SQLite | Debian copyright file (GPL-3.0-or-later and MPL-2.0) |
| godot | Godot | upstream repository license; games exported with it carry Godot's license notices |
| podman | Podman | Debian copyright file |
| podman | conmon, netavark, aardvark-dns | Debian copyright files |
| podman | crun | Debian copyright file (GPL-2.0+ and LGPL-2.1+) |
| podman | passt | Debian copyright file (GPL-2.0+ and BSD-3-Clause) |
| 2d | GIMP | Debian copyright files of gimp and gimp-data (GPL-3+, GPL-2+, LGPL-3+, MIT, Expat and BSD-2-clause code; artwork under CC-BY-SA-3.0, CC-BY-SA-4.0 and CC0-1.0); searched for non-commercial or similar terms, none found |
| 2d | Krita | Debian copyright files of krita and krita-data (GPL, LGPL, BSD, BSL-1.0, NCSA, Unlicense, CC-BY-SA-3.0, CC0-1.0, public domain); searched for non-commercial or similar terms, none found |
| 3d | Blender | the `license` folder inside the official release archive (`license.md`, `licenses.json`): Blender is GPL-3.0-or-later and the libraries inside it with their licenses are listed there; searched for non-commercial or similar terms, none found |

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
