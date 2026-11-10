# Bundles

Covers `IBundle` and its implementations, `BundleEngine`, `BundleArchive`, `BundleFiles` and `BundleInfo`: how a bundle is defined, how it becomes a file and how a file becomes an install.

## A bundle is a definition with three verbs

An `IBundle` has a name, a description, a target (the system it is made for and has to be installed on, `Ubuntu 24.04` unless a bundle says otherwise), an order, the bundles it requires and three operations: `Build` (download into a folder and report its components), `Install` (given the unpacked payload, change the computer) and `Uninstall`. The NuGet-only bundles (`xunit`, `avalonia`, `protobuf`, `sqlite`) share `NuGetBundleBase` and differ only in their package list. `BundleCatalog` orders them by `Order`, which is also the order `build all` and requirement installs use.

## The file

A bundle file is a pax tar named `<name>.bundle`. The first entry is `bundle.info` (name, build time, components; a bundle has no version of its own), the second `SHA256SUMS` (`hash  ./path`), then the payload. Putting the metadata first lets `list` read a multi-gigabyte file by reading only its first few entries. The format matches what the earlier shell version wrote, so files built before the app existed install unchanged, and a `revision=` line in an older file is ignored.

## Build and replace

Build runs in a staging folder. `IsUnchanged` lets a bundle skip the download when a cheap upstream check already proves nothing changed (the model bundle compares the registry manifest hash). Otherwise the payload is built, checksummed and compared with the checksums in the existing file; equal means the file is left as it is, different means the file is replaced (written next to it as `.partial` first and moved over it). Resumable staging entries (the model download's partial files) survive a failed or cancelled build so the next one continues.

Install takes the file of the bundle. A bundle that is installed already is skipped with a note to reinstall it. Reinstall checks everything before it changes anything (every file must be there, no other installed bundle may need one that is removed), asks once, uninstalls the ticked bundles that are installed (dependents first) and installs all of them in dependency order. A bundle that needs the one being reinstalled and is not part of it makes the whole reinstall refuse. Services inside a bundle are compared one by one with what the computer has; see `Decisions`.

## Install flow

1. Plan: the requested bundles plus the missing bundles they require, requirements first; every file must exist.
2. Confirm (the command line asks, the window has already asked).
3. Per bundle: warn when the computer runs another system than the bundle's Target (from `/etc/os-release`), check free space in the unpack folder, extract with a path-safety check (entries that resolve outside the folder abort), verify every checksum, run the bundle's `Install`, record the accounts, delete the unpacked copy.
4. A damaged file aborts before anything on the computer is changed.

Uninstall refuses while an installed bundle requires the target, removes dependents first when several are named, runs the bundle's `Uninstall` and deletes its record.
