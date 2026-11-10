# Apt

Covers `AptBuilder`, `AptInstaller` and `DebIndex`: carrying Ubuntu packages to a computer with no network.

## Build

`AptBuilder.Prepare` creates an apt root in the cache that points at the Ubuntu 26.04 (`resolute`) archive plus any extra sources a bundle names (the git-core PPA, an NVIDIA series). The plan comes from `apt-get -s install` against a fresh-system model, so the host's own packages never leak in. Packages with priority essential or required are left out when the archive has the release version, and bundled when the archive has a newer one. Each bundle splits its list into `top-level` packages (the services: their installed versions are compared and reported) and `support` packages (installed only when missing). A package that comes from the updates pocket is bundled together with its release version (except the services and the packages of the base system): a computer that has not installed the update may have packages that pin the release version of a library exactly (`python3.14` pins `libpython3.14-stdlib`), and with both versions in the repository apt can pick the one that fits instead of upgrading the library. `apt-get download` fetches the `.deb` files and `DebIndex` writes `Packages` and a `Release` without a date, so the same inputs give the same bytes and revisions stay stable.

## Install

The bundled folder is exposed as a `[trusted=yes] file:` source (symlinked from `/run`, which apt's sandbox user can read) and apt installs from it with no other source enabled and with `--no-remove`, so an install can fail but can never remove a package that is on the computer. Which packages were new is recorded in `apt-new`; only those are removed on uninstall, together with orphaned dependencies they pulled in (`APT::AutoRemove::RecommendsImportant=false`) and, for a driver bundle, old kernels except the one running.

A package that another bundle still needs stays when a bundle is uninstalled. It is written to `apt-leftover` next to the bundles' records, so the uninstall of whichever bundle is last removes it too; without that, libraries shared by two bundles would stay behind when the one that installed them is removed first.
