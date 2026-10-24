using System.Diagnostics;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace DotNetPip.Tasks;

public class EnsurePythonEnvironment : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] PackageReferences { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public string PythonVersion { get; set; } = "3.11.5";

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Restoring Python Dependencies ===");

        try
        {
            string targetEnvPath = Path.Combine(OutputDirectory, "python_env");
            if (!Directory.Exists(targetEnvPath))
            {
                Directory.CreateDirectory(targetEnvPath);
            }

            List<string> packagesToInstall = new();
            foreach (var item in PackageReferences)
            {
                string packageName = item.ItemSpec;
                string version = item.GetMetadata("Version");
                
                string packageSpec = string.IsNullOrWhiteSpace(version) 
                    ? packageName 
                    : $"{packageName}=={version}";

                packagesToInstall.Add(packageSpec);
                Log.LogMessage(MessageImportance.High, $" -> Resolving PythonPackageReference: {packageSpec}");
            }

            if (packagesToInstall.Count > 0)
            {
                InstallPackagesViaPip(targetEnvPath, packagesToInstall);
            }

            GenerateDependencyManifest(targetEnvPath);

            Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Python Environment Restored Successfully ===");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private void InstallPackagesViaPip(string envPath, List<string> packages)
    {
        string sitePackagesDir = Path.Combine(envPath, "site-packages");
        Directory.CreateDirectory(sitePackagesDir);

        string args = $"install --target \"{sitePackagesDir}\" {string.Join(" ", packages)}";
        ProcessStartInfo psi = new()
        {
            FileName = "pip",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to launch 'pip' package installer.");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pip installation failed with code {process.ExitCode}:\n{error}");
        }

        Log.LogMessage(MessageImportance.Normal, output);
    }

    private void GenerateDependencyManifest(string envPath)
    {
        string manifestPath = Path.Combine(OutputDirectory, "python-deps.json");
        var manifestData = new
        {
            EnvironmentPath = envPath,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Packages = PackageReferences.Select(p => new
            {
                Name = p.ItemSpec,
                Version = p.GetMetadata("Version")
            })
        };

        string json = JsonSerializer.Serialize(manifestData, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }
}
