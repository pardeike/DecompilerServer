using System.Diagnostics;

namespace Tests;

internal static class TemporaryAssemblyBuilder
{
    public static string BuildLibrary(string rootDirectory, string assemblyName, string sourceCode)
    {
        var projectDirectory = Path.Combine(rootDirectory, assemblyName);
        Directory.CreateDirectory(projectDirectory);

        var projectPath = Path.Combine(projectDirectory, $"{assemblyName}.csproj");
        var sourcePath = Path.Combine(projectDirectory, "Library.cs");

        File.WriteAllText(projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <AssemblyName>__ASSEMBLY_NAME__</AssemblyName>
              </PropertyGroup>
            </Project>
            """.Replace("__ASSEMBLY_NAME__", assemblyName, StringComparison.Ordinal));

        File.WriteAllText(sourcePath, sourceCode);

        var startInfo = new ProcessStartInfo("dotnet", $"build \"{projectPath}\" -c Release --nologo --verbosity quiet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build process.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build temporary assembly '{assemblyName}'.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }

        var assemblyPath = Path.Combine(projectDirectory, "bin", "Release", "net10.0", $"{assemblyName}.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Built assembly not found: {assemblyPath}");
        }

        return assemblyPath;
    }
}
