# Usage

Runnable examples of the app in different situations.

## Build a bundle on the online computer

```
./Bundler build git
```

Writes `git.bundle` into the `Bundles` folder next to the app (`--bundles PATH` picks another folder). The app asks for the administrator password first, because it always runs as root. Building `all` builds every bundle. Building again replaces the file with one built from the latest versions; when nothing upstream changed the file is left as it is.

## Move it to the offline computer

Copy the `.bundle` files you want into the `Bundles` folder next to the app on the offline computer. A file with the same name is replaced.

The app can also run straight from a disc or stick that holds `Bundler` and a `Bundles` folder side by side: it finds `Bundles` next to itself, whatever the mount point. Nothing is copied beforehand, but an install unpacks each bundle into `/var/tmp` first, so the computer needs the bundle's size plus 0.5 GB free there (the `gemma` model, 18.7 GB, then moves into place and needs only 2 GB more when `/var/tmp` and `/usr/share/ollama` are on the same disk, the whole model size again when they are not). If the medium is mounted `noexec`, copy `Bundler` to the computer and start it with `--bundles` pointing at the folder on the medium.

## Install

```
./Bundler install vs-code
```

Installs `git`, `dotnet` and `ollama` first when `vs-code` needs them and they are not installed yet (its file must be in the folder too). A bundle that is installed already is skipped (see Update). Every service is compared with what is on the computer and the log says what happened to each: installed, replaced with the newer one, skipped (same version) or kept (the computer has a newer one). Per-user setup (VS Code, NuGet and Cline settings and so on) is done for every normal user on the computer.

## Update

Bundles have no version numbers, so the app does not say which file is newer: the window and `list` (which also has a TARGET column) show when the bundle file was last changed, and you decide. To update, copy the new `vs-code.bundle` in and reinstall `vs-code` (`reinstall vs-code`, or tick it and press Reinstall in the window): it is uninstalled and installed again from the file. Reinstall refuses, before changing anything, if another installed bundle needs `vs-code` and is not reinstalled with it. Installing a bundle that is installed already is skipped.

## Uninstall

```
./Bundler uninstall vs-code
```

Removes only what the bundle itself installed or replaced. A bundle that another installed bundle needs is refused.

## The window

Run the app without arguments. The window is laid out like VS Code: the bundle list on the left (tick any number with the check boxes; each row shows the name and a badge: `missing` when there is no file in the Bundles folder, `available` when there is a file that is not installed yet, `installed`), the highlighted bundle's details on the right under the Build, Install, Uninstall and Cancel buttons, the output log at the bottom and a status bar that is gray when Ready and blue while Building, Installing or Uninstalling. The window has no title bar of its own: the minimize, maximize and close buttons are at the right end of the bar with the Build, Install, Uninstall and Cancel buttons, which is also where the window is dragged (double click maximizes) and they stay usable while an action runs. Dragging the dividers resizes the panels. While an action runs every control except Cancel is disabled.

The details show the bundle's name and badge, what it is for, the system it is made for (Target, `Ubuntu 26.04` for every bundle so far), when the bundle file was last changed (Bundle File), its dependencies and its services table (the table of `Bundles.md`: service, version, description, licenses, usage). The Reinstall button in the top bar uninstalls the ticked bundles that are installed and installs all the ticked ones again, asking once. Clicking a license opens its full text in a viewer window, with one tab for each license of that service.

## Things to know

- Workspace trust is off in VS Code (`security.workspace.trust.enabled=false`); without it the C# extension runs in limited mode. Remove that line from `App/src/Resources/vs-code/vscode-settings.json` before building if you want the prompts.
- VS Code settings are merged into each user's `settings.json`; comments in an existing file are dropped (a copy is kept as `settings.json.bundler.bak`).
- `dotnet new xunit3` defaults to net8.0; use `dotnet new xunit3 -f net10.0`, the SDK installed has only the .NET 10 runtime.
- The C# extension tries to download a Blazor WebAssembly debugging helper on first start and logs an error when offline; everything else works.
- The `a2000` bundle brings the kernel its signed modules were built for, so restart afterwards. It has not been run on a real RTX A2000 or with Secure Boot enforced; check `nvidia-smi` after the restart.
- The `auto-remote` bundle is pinned to Auto Remote 1.2.0 and needs `git`, `dotnet`, `ollama`, `gemma` and `open-code`, so a workspace set to OpenCode runs the local Gemma model with no network (the server's default `localModelUrl` is the local Ollama, and `opencode` is on its path). OpenCode follows the newest stable 2.x release, because Auto Remote 1.2.0 drives OpenCode 2.x and GitHub's "latest" for that repository is a 1.x release. It sets up the server as a systemd user service for every normal account (config in `~/.config/auto-remote/config.json`, created with a new key when missing and never overwritten; the second account's server uses the next port after 52679; its data is in `~/.local/share/AutoRemote`, which the bundle makes sure exists so the data does not move after a first login) and puts the desktop client in the app finder. The server's voice input model is downloaded from the internet when it starts, so without a network there is no voice input until a model file is named in `speechModel` in the config; everything else works offline. An uninstall keeps each account's config and data.
- Podman is the Ubuntu 26.04 archive version (5.7.0), not upstream (6.x), because upstream publishes only the remote client for Linux.
- Avalonia's `dotnet new` templates are modified at build time: the stock ones reference the paid-tool bridge package `AvaloniaUI.DiagnosticsSupport`, which has no stated license.
- Gemma 4 26B needs about 24 GB of RAM or more. Ollama uses a GPU when the NVIDIA driver is installed (the `a2000` bundle).
