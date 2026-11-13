# Bundler

[![Release](https://img.shields.io/github/v/release/Blue-Heighliner/Bundler)](https://github.com/Blue-Heighliner/Bundler/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/Blue-Heighliner/Bundler/actions/workflows/build.yml/badge.svg)](https://github.com/Blue-Heighliner/Bundler/actions/workflows/build.yml)
![Coverage](.github/badges/badge_linecoverage.svg)

Bundler is a desktop app (and command line) that builds **bundles** on an online computer and installs, updates and removes them on an offline Ubuntu 24.04 computer. A bundle is one file (`git.bundle`, `dotnet.bundle`, ...; or numbered parts when built with a size limit) holding everything it needs: Ubuntu packages, binaries, VS Code extensions, NuGet packages, models. It is built with [Avalonia](https://avaloniaui.net/) on .NET 10 as a self-contained single-file executable; the third-party licenses it ships with are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). For internal use inside one organization only, see [Bundles.md](Bundles.md) for what each bundle installs and under which licenses.

## Requirements

- Linux, x86-64, glibc based, to build or install bundles. Installing needs Ubuntu 24.04 (amd64) with the usual base system (`apt`, `tar`, `sudo` or `pkexec`) and free space in `/var/tmp` about the size of the bundle. The Windows build only shows the bundle list and explains that it cannot do the work. No separate .NET runtime install needed - the executable is self-contained.
- To build: internet access, `apt-get`, `apt-cache` and `dpkg-deb` for bundles that use Ubuntu packages, and free disk space of about twice the bundle size (the `gemma` bundle is about 19 GB).

## Installing

Download the executable for your platform from the latest [Release](https://github.com/Blue-Heighliner/Bundler/releases/latest):

- **Windows** - download `Bundler.exe` and run it.
- **Linux** - download `Bundler`, mark it executable (`chmod +x Bundler`), and run it.

## Using it

The app always runs as root: started as anyone else it asks for the password (`sudo` in a terminal, `pkexec` otherwise) and starts itself again, so it has one app data folder (root's) for its cache and install records. The bundle files are in a `Bundles` folder next to the executable (`--bundles PATH` uses another folder). **Build** on the online computer writes `<name>.bundle` into it, replacing the file that is there; copy the files you want into the `Bundles` folder next to the app on the offline computer and **Install** them there. To move a computer to a newer bundle file, reinstall the bundle. Running the app without arguments opens the window; with arguments it is a command line (`Bundler list`, `build git`, `install vs-code`, `uninstall vs-code`). Tick any number of bundles in the window, but only one action (build, install or uninstall) runs at a time. See [Docs/Usage.md](Docs/Usage.md) for the details.

## Documentation

| File | What it covers |
|---|---|
| [Bundles.md](Bundles.md) | Every bundle: what it installs, license and allowed usage of each component |
| [Docs/Api.md](Docs/Api.md) | The command line, the bundle file format and where the app keeps its files; running as root |
| [Docs/Usage.md](Docs/Usage.md) | Worked examples: building, moving, installing, updating, reinstalling, uninstalling, the window |
| [Docs/Architecture.md](Docs/Architecture.md) | Why the app is built the way it is |
| [Docs/Project.md](Docs/Project.md) | Scripts, publishing and workflows of this repository |
| [Docs/Components/](Docs/Components/) | Internals: the bundle file and installing, apt, NuGet mirror, install state, the window |
