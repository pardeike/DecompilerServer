namespace Tests;

internal static class TestAssemblyLocator
{
    public static string GetPath()
    {
        var assemblyPath = typeof(global::TestLibrary.SimpleClass).Assembly.Location;

        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Test library not found at: {assemblyPath}");
        }

        return assemblyPath;
    }
}
