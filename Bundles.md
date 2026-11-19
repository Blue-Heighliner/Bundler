# Bundles

Every bundle Bundler can build and install. Each has a Target, the system it is made for; all of them target Ubuntu 24.04 and an install on another system only warns. More exactly, they are built for the one system the installer USB drive puts on a computer: Ubuntu 24.04.5 desktop with the HWE kernel 7.0.0-31-generic. A build works out which packages that exact system lacks, so a bundle holds only those (and newer versions of packages the system has that the bundle needs), and an install adds them without removing anything. A bundle is built on an online computer into one file, `<name>.bundle` (numbered parts `<name>.bundle.001`, ... when built with a size limit), in the `Bundles` folder next to the app; that file is copied into the `Bundles` folder next to the app on the offline Ubuntu 24.04 computer. A build downloads the latest stable release of each component, so a file holds the versions that were latest when it was built (the exceptions, where a component only works in one line, are named in the bundles below: OpenCode 2.x, the .NET 10.0 and 11.0 channels the xunit v3 3.x line that `Avalonia.Headless.XUnit` needs, next to the 4.x of the xunit bundle, and the NVIDIA series whose modules fit the kernel). Bundles have no version numbers of their own, only the versions of the services inside them (the Version column below, which are the ones of the files built between 2026-10-28 and 2026-11-17). Whether a file is newer than what is installed is for the person to judge, from when the file was last changed; moving to it means reinstalling the bundle, which uninstalls it and installs the new file.

Scope: internal use inside one organization, on machines it owns, nothing redistributed (see `AGENTS.md`). **License** lists the licenses by short name; in the app, clicking one opens its full text (`licenses/texts/`). The text of a standard license is the standard text, the copyright line of each project is in its package. **Usage** says "Free use" when the licenses put no restriction on that scope, otherwise it lists the restrictions. "License review" below says where the licenses were read. This is not legal advice.

How installs treat what is already on the computer: a missing component is installed, an older one is replaced, the same version is skipped, and a newer one is kept (it is never replaced by an older one). NuGet packages are only ever added: older versions stay in the feed. An uninstall removes only what the bundle itself installed or replaced.

| Bundle | Needs | Installs |
|---|---|---|
| [`git`](#git) | none | git |
| [`a2000`](#a2000) | none | NVIDIA driver for the RTX A2000 |
| [`dotnet`](#dotnet) | none | .NET 10 and 11 SDKs, offline NuGet feed |
| [`ollama`](#ollama) | none | Ollama local model server as a systemd service |
| [`vs-code`](#vs-code) | `git`, `dotnet`, `ollama` | VS Code with C#, Git Graph, .NET Install Tool and Cline |
| [`python`](#python) | none | Python 3.15 with pip, venv and the headers |
| [`node`](#node) | none | Node.js with npm and npx |
| [`gemma`](#gemma) | `ollama` | Gemma 4 26B-A4B model for Ollama and Cline |
| [`chroma-db`](#chroma-db) | `ollama` | ChromaDB vector database as a systemd service, Nomic embedding model for Ollama |
| [`open-code`](#open-code) | `git` | OpenCode AI coding agent |
| [`xunit`](#xunit) | `dotnet` | xunit v3, Moq, coverlet.collector NuGet packages |
| [`avalonia`](#avalonia) | `dotnet` | Avalonia, CommunityToolkit.Mvvm, Tmds.DBus.Protocol and more NuGet packages, `dotnet new` templates |
| [`protobuf`](#protobuf) | `dotnet` | protobuf-net NuGet package |
| [`sqlite`](#sqlite) | `dotnet` | SQLite NuGet packages, DB Browser for SQLite |
| [`godot`](#godot) | `dotnet` | Godot game engine (.NET edition) |
| [`podman`](#podman) | none | Podman container engine |
| [`gimp`](#gimp) | none | GIMP photo editor |
| [`krita`](#krita) | none | Krita painting program |
| [`libre-office`](#libre-office) | none | LibreOffice Writer, Calc, Impress, Draw, Math and Base |
| [`blender`](#blender) | none | Blender 3D modeling, animation and rendering |
| [`xwayland`](#xwayland) | none | Xwayland, the X server for Wayland desktops |
| [`virtual-testing`](#virtual-testing) | none | Xvfb, xauth, Openbox, Cage, xdotool, wtype, wlrctl, scrot, grim and ImageMagick for testing on virtual screens |
| [`auto-remote`](#auto-remote) | `git`, `dotnet`, `ollama`, `gemma`, `open-code` | Auto Remote server as a systemd user service and its Linux desktop client with an app finder entry |

A bundle needs another only when its content or its setup uses it: the NuGet bundles and `godot` add to the feed of `dotnet`; `vs-code` configures the .NET SDK, git and Ollama; `gemma` and `chroma-db` bring models for `ollama`; `open-code` runs git; `auto-remote` runs git, scripts with `dotnet` and workspaces on the local model through `open-code` and `ollama`. `gemma` does not need `vs-code` (its Cline settings are only files), and nothing needs `a2000` (Ollama uses a GPU driver when there is one and runs on the CPU without).

`install <bundle>` installs the bundles it needs first if they are not installed yet (their files must be in the Bundles folder), and a bundle cannot be uninstalled while another installed bundle needs it.

## git

The latest stable git, from the official git-core PPA built for Ubuntu 24.04. Replaces an older git from the Ubuntu archive.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| git | 2.56.1 | Source control. Replaces an older git from the Ubuntu archive. | GPL-2.0, LGPL-2.1, MIT, ISC | Free use. |
| Ubuntu libraries | - | Runtime libraries git needs (curl, perl, expat, ...). | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## a2000

The NVIDIA Linux driver for the RTX A2000. The kernel modules are prebuilt and signed by Canonical, so they load with Secure Boot on and need no compiler, but each build is for one kernel. The bundle brings the modules for the kernel of the target system (7.0.0-31-generic) and no kernel of its own. The series is the newest one, at most the series NVIDIA lists as its latest stable release, whose driver packages and modules for that kernel fit together: Ubuntu stops building modules for a kernel once a newer one is out, so the newer series (595, 610, 615) have none that fit 7.0.0-31 and the build takes 580 (580.178.04 at the last build; NVIDIA's own latest is 595). The build says which series it skipped and why. After the install, restart; the install warns if the computer runs another kernel than the one the modules are for, because the driver only loads with that one.

The install reports the NVIDIA GPU it finds and warns if there is none or if it is not an RTX A2000 (it installs anyway). A newer driver that is already installed is kept. An older driver of another series cannot be installed next to the bundle's, so its packages are removed first, logged, and not brought back by an uninstall; the same series is simply upgraded. The driver itself could not be tried on a real A2000 (see `Docs/Usage.md`).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| NVIDIA driver | 580.178.04 | Userspace driver for the display and for compute (`nvidia-driver-580-open`, libnvidia-*, `nvidia-utils` with `nvidia-smi`, firmware). | NVIDIA-Driver-License | Only on infrastructure the organization owns or leases, and only with NVIDIA hardware (an RTX A2000 qualifies). Binaries must stay unmodified. Must not be distributed outside the organization. Section 16 requires notices when it is delivered to the U.S. Government. |
| Open kernel modules | 580 | Prebuilt GPU kernel modules for kernel 7.0.0-31-generic (`linux-modules-nvidia-580-open-7.0.0-31-generic`), signed by Canonical Ltd. Kernel Module Signing. DKMS (a dependency of the driver) finds the same module version already in place and does not replace it. | MIT, GPL-2.0 | Free use. |

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
| Visual Studio Code | 1.146.0 | Code editor. | VSCode-License | Only to develop and test the organization's own applications, including inside its internal network. Must not be shared, published or offered to others. |
| C# extension | 2.160.4 | C# language support in VS Code, with its Roslyn language server and debugger. The C# Dev Kit is left out on purpose, see below. | CSharp-Extension-License | Only with VS Code or Visual Studio, to develop and test the organization's own applications. Must not be shared outside the organization. The bundled debugger has its own Microsoft terms of the same kind. |
| .NET Install Tool | 3.2.0 | Lets the C# extension find the installed .NET (`ms-dotnettools.vscode-dotnet-runtime`). | MIT | Free use. |
| Git Graph | 1.30.0 | Commit graph view in VS Code (`mhutchie.git-graph`). | GitGraph-License | MIT with an added sentence: "Permission is NOT GRANTED to publish, distribute, sublicense, and/or sell derivative works". Use, copy, modify and merge are allowed. Do not distribute modified copies outside the organization. |
| Cline | 4.1.26 | Coding agent for VS Code that works through the local Ollama (`saoudrizwan.claude-dev`). Its cloud sign-in and hosted models are not used. | Apache-2.0 | Free use. |
| Support libraries | - | xdg-utils and ca-certificates for VS Code; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. |

## python

The newest stable CPython (3.15) from python-build-standalone, an Astral project that builds Python so that it needs nothing from the computer but the C library, with pip, venv, the headers and Tcl/Tk. It is installed in `/opt/python/<version>` with the commands `python`, `python3.15`, `pip` and `pip3.15` in `/usr/local/bin`. The command `python3` stays the system's Python 3.12, which the operating system itself uses. A Python of the same or a newer version that is already there is kept. Without a network, `pip` installs from wheels that were brought along (`pip install --no-index --find-links FOLDER NAME`); the wheels of the `chroma-db` bundle are an example.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| CPython | 3.15.2 | The Python interpreter and its standard library, with the development headers. | Python-2.0 | Free use. |
| pip | 26.3 | The package installer, with the libraries it carries (CacheControl, certifi, distlib, idna, packaging, platformdirs, Pygments, requests, resolvelib, rich, tomli, truststore, urllib3 and others). | MIT, Apache-2.0, BSD-3-Clause, BSD-2-Clause, ISC, MPL-2.0, PSF-2.0 | Free use. |
| Libraries built in | - | OpenSSL 3, SQLite, zlib, bzip2, xz, zstd, libffi, expat, libedit, ncurses, mpdecimal, libuuid, Berkeley DB (for the `dbm` module), Tcl/Tk and the X11 client libraries. | Apache-2.0, 0BSD, BSD-2-Clause, BSD-3-Clause, MIT, Sleepycat, TCL, X11, Zlib, bzip2-1.0.6 | Free use. The Berkeley DB license (Sleepycat) asks for the source of a program that contains it when that program is passed on; nothing is passed on. |

## node

The newest stable Node.js (26.13.1, which is also the Lithium long-term-support line) from nodejs.org, with npm and npx (the Node.js 26 archive no longer includes corepack). It is installed in `/opt/node/<version>` with the commands `node`, `npm` and `npx` in `/usr/local/bin`. A Node.js of the same or a newer version that is already there is kept. npm installs from a registry, so without a network packages have to be brought along (`npm install ./package.tgz`).

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Node.js | 26.13.1 | The JavaScript runtime with V8 and the libraries built into it (ICU, OpenSSL, libuv, undici, nghttp2, zlib, brotli, zstd, simdjson and others). | MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, Zlib, CC0-1.0, Unicode-3.0, BlueOak-1.0.0 | Free use. |
| npm | 11.20.0 | The package manager and its `npx` command. | Artistic-2.0, MIT, ISC | Free use. |

## gemma

Gemma 4 26B-A4B for Ollama, set as the model of Cline (32K context; the Cline settings are written whether or not VS Code is installed). Needs roughly 24 GB of RAM or more. An identical model that is already installed is skipped; a changed model of the same name replaces the old one.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Gemma 4 26B-A4B | gemma4:26b | Large language model (25.2B parameters, Q4_K_M, 18.7 GB, needs Ollama 0.30.9 or newer). | Apache-2.0 | Free use. |

## chroma-db

The ChromaDB vector database and the Nomic embedding model (`nomic-embed-text`) that turns text into the vectors it stores. ChromaDB comes as Python wheels that install, with no network, into a virtual environment of its own in `/opt/chroma-db/venv`, with the `chroma` command in `/usr/local/bin`. It runs as the systemd service `chroma-db`, enabled at boot, as the `chroma` user on `127.0.0.1:8000` (nothing else on the network can reach it), with its data in `/var/lib/chroma-db` and no telemetry. The model goes into Ollama (needs the `ollama` bundle), which serves the embeddings at `http://localhost:11434`; ChromaDB's own default embedding model is downloaded from the internet, so collections are made with the Ollama embedding function and `nomic-embed-text:latest` (`chromadb.utils.embedding_functions.OllamaEmbeddingFunction`, which needs the `ollama` Python client that is installed with ChromaDB). An uninstall removes the service, the environment and the model and keeps the data folder and the `chroma` user while there is data in it.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| ChromaDB | 1.5.9 | The vector database (a Rust server with a Python client), as wheels for Python 3.12. | Apache-2.0 | Free use. |
| Python packages | - | The packages ChromaDB needs and the `ollama` Python client, as wheels (numpy, onnxruntime, pydantic, grpcio, tokenizers, uvicorn, opentelemetry, kubernetes, aiohttp and others); the license of each is in `python-licenses.tsv` inside the bundle. | Apache-2.0, MIT, BSD-3-Clause, BSD-2-Clause, ISC, PSF-2.0, MPL-2.0, 0BSD, Zlib, CC0-1.0 | Free use. |
| Nomic embed text | nomic-embed-text:latest | Text embedding model (v1.5, 137M parameters, F16, 274 MB) for Ollama, from Nomic AI. | Apache-2.0 | Free use. |
| Python virtual environments | 3.12.3 | `python3.12-venv` with the pip and setuptools wheels it needs (Ubuntu 24.04 packages); installed only when missing. | Python-2.0, MIT, Apache-2.0, BSD-3-Clause, MPL-2.0, ISC, LGPL-2.1 | Free use. The license differs per package. |

## open-code

OpenCode, the AI coding agent for the command line, which runs the models of the local Ollama (such as `gemma`). It comes from the releases of `anomalyco/opencode` (the baseline x64 build, which runs on every x86-64 CPU), checked against the SHA-256 checksum GitHub publishes. Auto Remote drives OpenCode 2.x, so the newest stable 2.x release is taken (the repository's "latest" release is a 1.x one). It is installed in `/opt/open-code` with an `opencode` command in `/usr/local/bin`; an OpenCode that is the same or newer is kept. Needs `git`, which OpenCode runs for project detection and file status.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| OpenCode | 2.3.2 | AI coding agent (a single self-contained program). | MIT | Free use. |


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
| xunit v3 (3.x line) | 3.2.2 | The xunit v3 packages (`xunit.v3`, `.core.mtp-v1`, `.assert`, `.common`, `.extensibility.core`, `.runner.*`, `.mtp-v1`) and `xunit.runner.visualstudio` 3.1.5, because `Avalonia.Headless.XUnit` needs the 3.x line and fails at test discovery with the 4.x one that the `xunit` bundle brings. A project that tests Avalonia windows references `xunit.v3` 3.2.2. | Apache-2.0 | Free use. |

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

## libre-office

LibreOffice (24.2) from the Ubuntu 24.04 archive: Writer (documents), Calc (spreadsheets), Impress (presentations), Draw (drawings), Math (formulas) and Base (databases), with Base's embedded Firebird database engine, Python macros and the English grammar checker. The installer USB drive already has every tool but Base, so the bundle adds Base and brings the others to the archive's newest version. Java is not included (the HSQLDB engine and the few functions that need a Java runtime do not work); the embedded Firebird engine is what Base uses without it.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| LibreOffice | 24.2.7 | Writer, Calc, Impress, Draw, Math and Base (`libreoffice-writer`, `-calc`, `-impress`, `-draw`, `-math`, `-base`) and the parts they share. | MPL-2.0, LGPL-3.0, GPL-3.0, GPL-2.0, MIT, Apache-2.0, BSD-3-Clause, BSD-2-Clause, CC0-1.0, CC-BY-SA-3.0 | Free use. The license differs per file; the main license is MPL-2.0. |
| Firebird embedded | 3.0.11 | Base's database engine (`libreoffice-sdbc-firebird`, `firebird3.0-server-core`, `libfbclient2`, `libtommath1`). | IDPL-1.0, IPL-1.0, Unlicense | Free use. |
| Python macros and grammar checker | 24.2.7 | `libreoffice-script-provider-python` and `libreoffice-lightproof-en`. | MPL-2.0, LGPL-3.0, GPL-3.0 | Free use. |

## blender

Blender, the 3D modeling, sculpting, animation and rendering program, which exports glTF, FBX, OBJ and STL for games and apps (such as Godot). It comes from Blender's own Linux release archive, because the Ubuntu 24.04 archive is far behind (4.0.2 against 5.3.0 at the last build); the archive is checked against the SHA-256 checksum Blender publishes. It is installed in `/opt/blender` with a `blender` command and a menu entry, and an install of a newer Blender replaces the older one. The archive carries its own license folder, which lists every library built into Blender; it was searched for non-commercial, research-only or similar terms and has none.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Blender | 5.3.0 | 3D modeling, sculpting, animation and rendering program. | GPL-3.0 | Free use. |
| Libraries built into Blender | 5.3.0 | Image, video, geometry, physics and scripting libraries. | Apache-2.0, BSD-2-Clause, BSD-3-Clause, BSL-1.0, GPL-2.0, LGPL-2.1, LGPL-3.0, MIT, MPL-2.0, NCSA, Python-2.0, Zlib | Free use. |
| Smaller library licenses | 5.3.0 | Licenses of smaller libraries and code in Blender (compression, PNG and TIFF, FreeType, OpenGL headers and others). | 0BSD, bzip2-1.0.6, FTL, libpng-2.0, libtiff, MIT-Khronos-old, SGI-B-2.0, TOST-1.0 | Free use. |
| Fonts | 5.3.0 | The fonts of Blender's interface and text objects. | OFL-1.1, Bitstream-Vera, Arev-Fonts | Free use. |
| Support libraries | - | X11, OpenGL, Wayland and other system libraries the program needs, and xz to unpack it; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause | Free use. The license differs per package. |

## xwayland

Xwayland (23.2.6), the X server that lets X11 programs run on a Wayland desktop, from the Ubuntu 24.04 archive. The installer USB drive has it already; the bundle brings the archive's newest version and anything that version needs.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Xwayland | 23.2.6 | The X server for Wayland desktops (`xwayland`). | MIT | Free use. |

## virtual-testing

The packages for testing programs automatically on virtual screens, from the Ubuntu 24.04 archive: Xvfb with xauth and the Openbox window manager (a virtual X11 screen), Cage (a virtual Wayland screen), the tools that type, click and move windows (xdotool for X11, wtype and wlrctl for Wayland) and the tools that take and compare pictures of the screen (scrot for X11, grim for Wayland, ImageMagick). Nothing starts by itself: a test starts what it needs, for example `Xvfb :99 &` and `DISPLAY=:99 openbox &`, or `WLR_BACKENDS=headless cage -- PROGRAM`.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Xvfb | 21.1.12 | Virtual X11 screen (`xvfb`, from the X.Org server). | MIT | Free use. |
| xauth | 1.1.2 | Keeps the X11 access keys that Xvfb needs. | MIT | Free use. |
| xdotool | 3.20160805.1 | Types, clicks and moves windows on an X11 screen. | BSD-3-Clause | Free use. |
| scrot | 1.10 | Screenshots of an X11 screen. | MIT, BSD-3-Clause | Free use. |
| Openbox | 3.6.1 | A small window manager for the virtual X11 screen. | GPL-2.0, GPL-3.0, BSD-3-Clause | Free use. |
| Cage | 0.1.5 | A kiosk Wayland compositor that runs one program, also on a virtual (headless) screen. | MIT | Free use. |
| wtype | 0.4 | Types on a Wayland screen. | MIT | Free use. |
| wlrctl | 0.2.2 | Controls windows on a Wayland screen. | MIT | Free use. |
| grim | 1.4.0 | Screenshots of a Wayland screen. | MIT | Free use. |
| ImageMagick | 6.9.12 | Converts and compares pictures (`convert`, `compare`, `identify`). | Apache-2.0, GPL-2.0, LGPL-3.0 | Free use. The ImageMagick license is the Apache license with a preamble. |
| Libraries | - | wlroots, libseat, Imlib2, libxcb parts and the other libraries these need; installed only when missing. | MIT, LGPL-2.1, GPL-2.0, CC0-1.0, BSD-3-Clause | Free use. The license differs per package. |

## auto-remote

The latest stable Auto Remote (1.3.10 at the last build, `Markwardt-Labs/Auto-Remote`): a server that hosts AI coding workspaces on this computer, and a desktop client for using them. Installed the way the author runs it: the server and the client are in `/opt/auto-remote`, the server is a systemd user service of every normal account (enabled and lingering, so it runs without a login), and the client has an app finder entry with its icon. A server's config, `~/.config/auto-remote/config.json`, is created with a new random key when it is missing (read the key there to connect a client) and is never overwritten; with several accounts the second one's server gets the next port after the default 52679, and so on. An uninstall removes the services, the programs and the entry, and keeps each account's config and its data in `~/.local/share/AutoRemote`. Needs `git` (for the server's git features), `dotnet` (the server runs scripts with `dotnet`), `ollama`, `gemma` and `open-code`, so that workspaces run the local Gemma model without any network: the install gives each account an AI environment (`~/.local/share/AutoRemote/environment.json`, only written when it is missing, readable by that account only) in which new workspaces use local models (OpenCode and the Ollama at `http://localhost:11434/v1`), `gemma4:26b` for every capability with a context of 16384, and `opencode` is on the server's path. A desktop client of the same account connects with its Connect to Local Server button, which needs no key: the server hands the key to the account through a socket in the account's runtime folder, so the server has to run as the same account (which the user service does). Claude Code and Codex (`claude`, `codex`) are not part of this bundle. The server downloads its voice input model (Whisper `small.en`, about 470 MB) from the internet in the background when it starts; without a network it runs fine (checked offline in a chroot) but has no voice input, until a `ggml` model file is named in `speechModel` in the config.

| Service | Version | Description | License | Usage |
|---|---|---|---|---|
| Auto Remote server | 1.3.10 | Command line server for the Android, desktop and browser clients, self-contained (.NET runtime, Live AI, Whisper.net, whisper.cpp and ggml inside). A systemd user service for every normal account. | MIT | Free use. |
| Auto Remote desktop client | 1.3.10 | Avalonia desktop app, self-contained, with an app finder entry and its icon. | MIT, BSD-2-Clause, BSD-3-Clause | Free use. |
| Support libraries | - | ICU and OpenSSL for the server, and X11, OpenGL, Vulkan, fontconfig (the install builds its font cache, without which the client aborts on its first start) and fonts for the desktop client; installed only when missing. | GPL-2.0, LGPL-2.1, MIT, BSD-3-Clause, Apache-2.0 | Free use. The license differs per package. |

## License review

Where a license text or a metadata field was read for a service, and where it was not read in full. Services that are not listed take their license from the package metadata (SPDX expression in a `.nuspec`, the repository's license or the Debian copyright file).

| Bundle | Service | Checked from |
|---|---|---|
| git | git | Debian copyright file (GPL-2.0, with LGPL-2.1+, Expat and ISC parts) |
| a2000 | NVIDIA driver | `licenses/NVIDIA-Driver-License.txt`; the 580 package (`nvidia-utils-580` copyright file, read in the 2026-11-15 rebuild) has 79 of 92 license paragraphs identical to the text on file; the other 13 are Debian packager commentary (a README question and answer and a 2003 email in which NVIDIA allows redistribution), not license terms |
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
| avalonia | xunit v3 3.x line, xunit.runner.visualstudio 3.1.5 | the same Apache-2.0 packages as the xunit bundle at an older version; the build's NuGet license check reported nothing to review |
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
| auto-remote | Auto Remote server and desktop client | the repository `LICENSE` (MIT) and the release's `THIRD-PARTY-NOTICES.txt` (both read again for 1.3.10; the notices list the .NET runtime, Live AI, Whisper.net, whisper.cpp, ggml and Microsoft.Extensions.AI.Abstractions, all MIT); the Whisper.net package's `LICENSE` read (MIT); the client's Avalonia (MIT), SkiaSharp and Skia (MIT, BSD-3-Clause) and Markdig (BSD-2-Clause) from their package metadata, not read in full, and the release's notices file does not cover the client; the voice model the server downloads at run time is not part of the bundle and was not reviewed |
| python | CPython and the libraries built in | `licenses/LICENSE.cpython.txt` and the other texts in the `licenses` folder of the full build of the same python-build-standalone release (20261110): Python-2.0 and CNRI-Python for the interpreter; OpenSSL 3 (Apache-2.0), SQLite (public domain), zlib, zlib-ng, bzip2, xz (0BSD), zstd, libffi, expat, libedit, ncurses (X11), mpdecimal, libuuid, libX11, libXau, libxcb, Berkeley DB (Sleepycat), Tcl/Tk and Tix (TCL) read; pip's own `licenses` folder inside the archive read for the packages it carries |
| node | Node.js and npm | the `LICENSE` file of the tarball, which lists the license of every bundled library (read for the components named in the table; npm is under the Artistic License 2.0, ICU under the Unicode license, minimatch under the Blue Oak Model License); none restricts commercial or government use |
| chroma-db | ChromaDB and its Python packages | the wheel metadata of all 84 wheels (SPDX expression, license field or classifiers, written into `python-licenses.tsv`; all permissive or MPL-2.0 at file level); `LICENSE` of the chromadb wheel not read in full |
| chroma-db | Nomic embed text | `LICENSE.txt` of the model in the Ollama registry (Apache-2.0, read) |
| chroma-db | Python virtual environments | Debian copyright files of `python3.12`, `python3-pip-whl`, `python3-setuptools-whl` and `python3-pkg-resources`, searched for non-commercial, research-only or similar terms and have none |
| libre-office | LibreOffice | Debian copyright file of `libreoffice-core`, shared by the other packages (MPL-2.0 with Apache-2.0, MIT, LGPL, GPL, CC0, CC-BY-SA-3.0 and BSD parts), searched for non-commercial, research-only or similar terms and has none |
| libre-office | Firebird embedded | Debian copyright file of `firebird3.0-common-doc` (IDPL-1.0 and IPL-1.0, whose texts are in it and in `licenses/texts/`; MPL-1.1, LGPL and public domain parts) and of `libtommath1` (public domain) |
| libre-office | Python macros and grammar checker | Debian copyright files (MPL 1.1, GPL-3+ and LGPL-3+ for Lightproof) |
| xwayland | Xwayland | Debian copyright file of `xwayland` (the X.Org Foundation's MIT license), searched for non-commercial terms and has none |
| virtual-testing | all packages | Debian copyright files of every package and of the ImageMagick, wlroots and X.Org libraries that come with them (Expat/MIT, BSD-3-Clause, GPL-2+, GPL-3+, LGPL-2.1+, LGPL-3+, CC0-1.0; ImageMagick is "Apache-2.0 with an additional preamble"; scrot's `MIT-feh` is an MIT variant), searched for non-commercial, research-only or similar terms and have none |

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
