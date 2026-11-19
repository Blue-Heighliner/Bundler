namespace BlueHeighliner.Bundler.Tests;

internal static class UninstallScriptExtensions
{
    extension(UninstallScript script)
    {
        public IReadOnlyList<string> Lines()
            => Flatten(script.Steps, string.Empty).ToList();
    }

    private static IEnumerable<string> Flatten(IEnumerable<UninstallStep> steps, string indent)
    {
        foreach (UninstallStep step in steps)
        {
            yield return indent + string.Join(' ', step.Args.Prepend(step.Name));
            foreach (string inner in Flatten(step.Then, indent + "  "))
            {
                yield return inner;
            }
        }
    }
}
