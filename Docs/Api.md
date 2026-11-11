# API

The surface of Bundler is its command line and the bundle files it reads and writes; the window is a front end to the same engine. This document covers the design and flow of that surface, not the internals.

## Shape

One executable. Without arguments it opens the window; with arguments it runs a command and exits with `0` (done), `1` (failed, the reason is on stderr) or `2` (usage error). Everything that changes the computer goes through the same engine in both front ends, so they behave the same. Log lines go to stderr in a fixed format (`==> ` headings, indented details, `WARNING:` and `ERROR:`) because the window reads exactly that format back from the elevated child process.

| Command | Where | What it does |
|---|---|---|
| `list` | anywhere | One row per bundle: `not built`, `available` (file in the Bundles folder) or `installed`, with its target system and when the bundle file was last changed. |
| `build <bundle...\|all>` | online computer | Downloads the latest stable version of everything the bundle needs and packs it into `<name>.bundle` in the Bundles folder, replacing the file there, or does nothing if the file already holds exactly that. |
| `install <bundle...>` | offline computer | Installs the bundle file found in the Bundles folder, after the bundles it needs. A bundle that is installed already is skipped: reinstall it to use a different file. |
| `reinstall <bundle...>` | offline computer | Uninstalls the bundles that are installed and installs all of them again from their files, asking once. Refuses before changing anything if another installed bundle needs one of them. |
| `uninstall <bundle...>` (`remove`) | offline computer | Removes what the bundle installed. Refuses while another installed bundle needs it. |

Options: `-y/--yes` (do not ask), `-f/--force` (install a bundle that is installed already, over it), `--bundles PATH` (the folder with the bundle files).

On Linux the app always runs as root. If it is started as anyone else it asks for the password (`sudo` in a terminal, `pkexec` otherwise), starts itself again as root with the same arguments and returns that run's exit code.

## Bundles folder

The Bundles folder is `Bundles` next to the executable, or the folder given with `--bundles PATH`. A bundle file is named `<name>.bundle`. Bundles have no revision numbers: a file holds the versions that were latest when it was built. To move a computer to a newer file, reinstall the bundle. See `Docs/Components/Bundles.md`.

## Where the app keeps its files

The app reads no environment variables as settings. Because it always runs as root, what it keeps for itself is in `BlueHeighliner.Bundler` inside root's app data folder (`/root/.config` on Linux):

| Folder | Content |
|---|---|
| `cache/` | Build cache: downloads, the private .NET SDKs used to mirror NuGet packages, apt state. |
| `state/` | What is installed, per bundle. |

The bundle files are in `Bundles` next to the executable, or in the folder given with `--bundles`; they are the only thing a person moves around. Fixed places elsewhere: `/var/tmp` for the scratch space of NuGet mirroring (about 2 GB while a bundle is built) and for unpacking a bundle while it installs, and `/opt/bundler/nuget-feed` for the offline NuGet feed.
