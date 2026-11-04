# Architecture

This document explains why Bundler is built the way it is.

## One engine, two front ends, always root

The window and the command line call the same `IBundleEngine`; neither has logic of its own beyond asking questions and showing the log. The alternative, a GUI that shells out to the command line, was rejected because cancellation, progress and errors would then have to be parsed from text.

On Linux the whole app runs as root. Started as anyone else, `Program` starts the same executable again through `sudo` (when there is a terminal to ask in) or `pkexec` and returns its exit code, forwarding the arguments, the absolute bundles folder and the display variables a window needs. The alternative, elevating only for install and uninstall, would leave two app data folders (the user's and root's), root-owned install records inside a user's home, and a different "installed" answer per user. Always running as root gives one place for state and cache, whoever starts the app. The cost is that the window itself runs as root: the window is an X11 client (through XWayland on a Wayland desktop) and an X server only lets root in when it is given the user's X authority file or told to, which is why the `XAUTHORITY` variable is forwarded to the root copy (GNOME's XWayland uses such a file, so the default Ubuntu desktop works) and why a compositor without one needs `xhost +si:localuser:root` (the app says so when it cannot connect), and files the build writes into the bundles folder are root-owned.

## Self-contained single file, Linux-only work

The app ships as one self-contained executable per platform so an offline machine needs no .NET runtime. The work (apt, systemd, chown, `/var/lib`) only exists on Linux, so the Windows build opens, lists and explains instead of failing in the middle of an install. Globalization is invariant so the binary also starts on a minimal Ubuntu without ICU.

## One file per bundle, no versions of bundles

A bundle is the file `<name>.bundle`, built from the latest stable versions of its services. There is no revision number: a second build replaces the file, and a build that would produce exactly the content of the existing file (same checksums) leaves it alone. The alternative, numbered revisions with upgrade and downgrade rules at install, needed the app to decide what is newer and to reconcile two files of the same bundle. Leaving that to the person keeps the app simple: it shows what is installed and what the file holds, install skips an installed bundle, and an update is a reinstall: an uninstall followed by an install of the new file.

## Ubuntu packages resolved in a private apt root

Packages are resolved against the Ubuntu 26.04 archive in a private apt root in the cache, so a build works from any Linux host and not only from 26.04. The build computes the closure of what a fresh 26.04 needs (leaving out Essential and required packages unless the archive has a newer version than the release), downloads the `.deb` files and indexes them as a flat local repository. Installing points apt at that folder only, so it works with no network and with apt's own dependency solver.

## NuGet mirrored with a real restore

A hand-written dependency walker would drift from NuGet's resolution rules. The build instead runs a real `dotnet restore` with a private latest-stable SDK against a scratch project and collects what the global packages folder ends up holding, including runtime packs, then lays it out as a v3 feed. Template packs stay in the bundle, not the feed, because `dotnet new` installs them from files.

## Installs only undo what they did

Every install writes records under `state/<bundle>/` in root's app data folder: what apt added, which packages were added to the feed, which templates and extensions, which settings values. Uninstall removes exactly those, so a component that was kept because the computer already had a newer one stays. Settings files are edited by key and only removed when the value is still the one the bundle wrote.

## Licenses are part of the build

NuGet licenses are read from every package at build time and anything outside a permissive SPDX allowlist is reported for a person to review; the result is kept in the bundle. Components other than NuGet are reviewed by a person and recorded in `Bundles.md`.
