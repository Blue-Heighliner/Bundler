# Working rules for this offline machine

This computer has no internet. Work only with what is installed.

- Read the relevant files before changing them. Prefer small, focused edits over rewriting files.
- After each change run `dotnet build` and fix the errors before going on. If the project already has tests and they restore, run `dotnet test` too.
- NuGet restore uses only the offline feed at `/opt/bundler/nuget-feed` (it is configured for every project already). Packages that are not in that feed cannot be restored. Do not add or upgrade packages to fix a restore failure, report it instead. `ls /opt/bundler/nuget-feed` shows what is available.
- The .NET 10 and .NET 11 SDKs are installed (`dotnet --list-sdks`; `dotnet` uses the newest, a `global.json` picks the other) and the feed supports projects that target `net10.0` or `net11.0` only. Do not target another framework such as `net8.0`; `dotnet new xunit3` defaults to it, so use `-f net10.0` (the xunit3 and Avalonia templates do not offer `net11.0`: change `TargetFramework` afterwards).
- Do not run commands that need a network (`curl`, `wget`, `apt`, `pip`, `npm`, `git clone`, `git fetch`, `git pull`, `git push`).
- Do not run `git commit`, `checkout`, `switch`, `branch`, `merge`, `rebase` or `reset --hard`. The user does that. `git status`, `git diff` and `git add` are fine.
- Follow the style of the code that is already there (naming, formatting, language features). The language is C# on the installed .NET SDK (`dotnet --version`) unless the project says otherwise.
- If the same error comes back three times, stop. Say what you tried and what you think is wrong, and wait.
- When the task is finished, give a short summary of what changed and what you checked.
