using System.Diagnostics;
using System.Text.Json;

namespace Civil3D.HybridMcp.Integration.Tests;

public sealed class PackagingDiagnosticsTests
{
    [Fact]
    public async Task CheckBundleInstallAcceptsValidBundleAndEmitsJson()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "check-bundle-install.ps1");
        Assert.True(File.Exists(scriptPath), $"Expected offline bundle diagnostic script at {scriptPath}.");

        using var bundle = CreateBundleFixture(root);

        var result = await RunPowerShellAsync(scriptPath, "-BundlePath", bundle.Path, "-AsJson");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(bundle.Path, document.RootElement.GetProperty("bundlePath").GetString());
        Assert.Empty(document.RootElement.GetProperty("issues").EnumerateArray());
        Assert.Contains(
            document.RootElement.GetProperty("requiredFiles").EnumerateArray(),
            file => file.GetString() == "Civil3D.HybridMcp.Tools.Survey.dll");
    }

    [Fact]
    public async Task CheckBundleInstallRejectsPackagedAutodeskManagedAssemblies()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "check-bundle-install.ps1");
        Assert.True(File.Exists(scriptPath), $"Expected offline bundle diagnostic script at {scriptPath}.");

        using var bundle = CreateBundleFixture(root);
        File.WriteAllText(Path.Combine(bundle.ContentsPath, "acmgd.dll"), string.Empty);

        var result = await RunPowerShellAsync(scriptPath, "-BundlePath", bundle.Path, "-AsJson");

        Assert.NotEqual(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var issues = document.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetString()).ToArray();
        Assert.Contains(issues, issue => issue is not null && issue.Contains("Autodesk managed assembly", StringComparison.OrdinalIgnoreCase));
    }

    private static BundleFixture CreateBundleFixture(string root)
    {
        var sourceManifest = Path.Combine(root, "packaging", "Civil3D.HybridMcp.bundle", "PackageContents.xml");
        var bundlePath = Path.Combine(Path.GetTempPath(), "Civil3D.HybridMcp.Packaging.Tests", Guid.NewGuid().ToString("N"), "Civil3D.HybridMcp.bundle");
        var contentsPath = Path.Combine(bundlePath, "Contents", "Win64");
        Directory.CreateDirectory(contentsPath);

        File.Copy(sourceManifest, Path.Combine(bundlePath, "PackageContents.xml"));

        foreach (var fileName in RequiredFiles)
        {
            File.WriteAllText(Path.Combine(contentsPath, fileName), string.Empty);
        }

        return new BundleFixture(bundlePath, contentsPath);
    }

    private static async Task<ProcessResult> RunPowerShellAsync(string scriptPath, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Civil3D.HybridMcp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static readonly string[] RequiredFiles =
    [
        "Civil3D.HybridMcp.Bridge.dll",
        "Civil3D.HybridMcp.Bridge.deps.json",
        "Civil3D.HybridMcp.Bridge.runtimeconfig.json",
        "Civil3D.HybridMcp.Core.dll",
        "Civil3D.HybridMcp.Diagnostics.dll",
        "Civil3D.HybridMcp.Protocol.dll",
        "Civil3D.HybridMcp.Sdk.dll",
        "Civil3D.HybridMcp.Tools.AutoCAD.dll",
        "Civil3D.HybridMcp.Tools.Civil.dll",
        "Civil3D.HybridMcp.Tools.Survey.dll",
        "Civil3D.HybridMcp.Tools.Roadway.dll"
    ];

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class BundleFixture : IDisposable
    {
        public BundleFixture(string path, string contentsPath)
        {
            Path = path;
            ContentsPath = contentsPath;
        }

        public string Path { get; }

        public string ContentsPath { get; }

        public void Dispose()
        {
            var root = Directory.GetParent(Path)?.FullName;
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
