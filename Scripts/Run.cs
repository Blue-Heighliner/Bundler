#:package Markwardt.ScriptUtilities@0.2.0
#:property TreatWarningsAsErrors=true

// Runs the app locally via `dotnet run`. File-based app (dotnet run) - run from the repo root,
// e.g. `dotnet run Scripts/Run.cs`. The app asks for administrator rights itself; its bundles folder is the repo's `bundles/`
// instead of the one next to the built app.

using Markwardt.ScriptUtilities;

(await Script.Run("dotnet", "run", "--project", "App/App.csproj", "--", "--bundles", Path.GetFullPath("bundles"))).Verify();
