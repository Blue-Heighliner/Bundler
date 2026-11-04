# Install state and elevation

Covers `StateStore`, `BundleState`, `Decisions`, `Users`, `UserFiles`, `JsonFiles`, `TextBlocks`, `Machine`, `Paths` and `Elevator`.

## Records

`state/<bundle>/` in root's app data folder holds an `installed` file (accounts, time) written last, so a half-finished install is not "installed", plus record files a bundle chooses (added apt packages, added feed packages, owned markers, the model name). A marker like `owned-dotnet` means "this bundle put it there", the condition for removing it later.

## Per-user setup

Settings for VS Code, NuGet and Cline are written for every normal account (UID 1000 and up with a home folder); a bundle is always installed for all of them. Files are created with the account's ownership. `JsonFiles` merges keys into JSONC settings files (comments and trailing commas tolerated; a backup is kept only when the original had them) and `UnsetMatching` removes a key only if it still has the value the bundle wrote. `TextBlocks` manages marked `# bundler <tag> begin/end` blocks in plain files such as `/etc/environment`.

## Running as root

`Machine.IsPrivileged` is true for root. On Linux `Program` checks it first: when it is false, `Elevator` runs the same executable again through `sudo env ...` (sharing the terminal, when stdin is a terminal) or `pkexec env ...` (output read back line by line, parsed with `LogFormat`), passing the original arguments, `--bundles <full path>` and the display variables (`DISPLAY`, `XAUTHORITY`, `WAYLAND_DISPLAY`, `XDG_RUNTIME_DIR`) because both programs reset the environment and the window would otherwise not find the display. The root copy resolves its data folder from root's entry in `/etc/passwd`, not from `HOME`, since `sudo` may or may not change `HOME`.
