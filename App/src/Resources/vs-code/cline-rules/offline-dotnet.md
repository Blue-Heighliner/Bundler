# Working rules for this offline machine

This computer has no internet. Work only with what is installed.

- Read the relevant files before changing them. Prefer small, focused edits over rewriting files.
- After each change run `dotnet build` and fix the errors before going on. If the project already has tests and they restore, run `dotnet test` too.
- NuGet restore uses only the offline feed at `/opt/bundler/nuget-feed` (it is configured for every project already). Packages that are not in that feed cannot be restored. Do not add or upgrade packages to fix a restore failure, report it instead. `ls /opt/bundler/nuget-feed` shows what is available.
- Do not run commands that need a network (`curl`, `wget`, `apt`, `pip`, `npm`, `git clone`, `git fetch`, `git pull`, `git push`).
- Do not run `git commit`, `checkout`, `switch`, `branch`, `merge`, `rebase` or `reset --hard`. The user does that. `git status`, `git diff` and `git add` are fine.
- Follow the style of the code that is already there (naming, formatting, language features). The language is C# on the installed .NET SDK (`dotnet --version`) unless the project says otherwise.
- If the same error comes back three times, stop. Say what you tried and what you think is wrong, and wait.
- When the task is finished, give a short summary of what changed and what you checked.
