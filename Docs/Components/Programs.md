# Programs that are not Ubuntu packages

Covers `PythonWheels`, `ToolInstaller` and `OllamaModelFiles`: how the `python`, `node`, `chroma-db` and `gemma` bundles carry what the Ubuntu archive does not provide. Everything is resolved or downloaded at build time and installs with no network.

## Python wheels

`PythonWheels` runs `pip download` for the Python of the target system (3.12, CPython ABI, every glibc tag up to the target's 2.39) with `--only-binary`, so a package without a wheel fails the build instead of needing a compiler on the offline computer. Every wheel's `METADATA` gives its license (an SPDX expression, a short `License` field or the classifiers, with a few known spellings mapped to SPDX); `NuGetLicenses.IsPermissive` judges it and anything else is reported as REVIEW unless the package is in `licenses/reviewed-python.tsv`. The table is written into the bundle as `python-licenses.tsv`. The bundle installs the wheels into a virtual environment of its own with `pip install --no-index --find-links`, so nothing touches the system Python.

## Archive tools

`ToolInstaller` unpacks an archive into `/opt/<program>/<version>` (dropping the archive's top folder, through a `.partial` folder that is renamed when complete) and links commands into `/usr/local/bin`. Versions sit next to each other; the newest folder is what counts as installed, and the decision logic (missing: install, older: replace by installing next to it, same: skip, newer: keep) lives in the bundle. A command that exists and is not a link into the program's folder is left alone and reported; a command whose file is not in the archive is skipped (Node.js 26 has no corepack). An uninstall removes the links that still point into the folder and the folder itself, and the program's folder when it is empty. The `python` bundle deliberately has no `python3` or `pip3` command: `python3` must stay the system's Python, which the operating system itself uses.

## Ollama models

`OllamaModelFiles` is the install and uninstall of a model that a bundle carries (`ollama-models` and `model.info` in its payload), shared by the `gemma` and `chroma-db` bundles. Files are moved, not copied, when the payload and Ollama's folder are on one disk, and are checked against the SHA-256 in their names when they are copied. An identical model is skipped, a changed model of the same name replaces the old one and the files nothing uses any more are removed, and a model that was already there before the bundle is never removed by it.
