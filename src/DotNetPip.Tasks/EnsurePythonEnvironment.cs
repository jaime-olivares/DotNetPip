using System.Diagnostics;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace DotNetPip.Tasks;

public class EnsurePythonEnvironment : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] PackageReferences { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public string PythonVersion { get; set; } = string.Empty;

    public string PythonExecutable { get; set; } = string.Empty;

    public string ForceRestore { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            if (!PythonVersionSpec.TryParse(PythonVersion, out PythonVersionSpec requested))
            {
                throw new InvalidOperationException(
                    "Set <PythonRuntimeVersion> in the .csproj (for example 3.11.5).");
            }

            List<string> packagesToInstall = CollectPackageSpecs();
            string targetEnvPath = Path.Combine(OutputDirectory, "python_env");
            bool force = IsTrue(ForceRestore);

            if (!force && IsEnvironmentCurrent(requested, packagesToInstall, targetEnvPath, out PythonInstall? cached))
            {
                StageNativeLibrary(cached!.Library, requested);
                Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Python environment is up to date ===");
                return true;
            }

            Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Restoring Python Dependencies ===");

            string pythonExe = ResolvePythonExecutable(requested);
            PythonInstall install = ProbePython(pythonExe);
            if (!PythonVersionSpec.TryParse(install.Version, out PythonVersionSpec actual)
                || actual != requested)
            {
                throw new InvalidOperationException(
                    $"PythonRuntimeVersion is {requested.Dotted} but the interpreter at '{install.Executable}' is {install.Version}. Install Python {requested.Dotted} or set DOTNETPIP_PYTHON.");
            }

            Log.LogMessage(MessageImportance.High, $" -> Using Python {install.Version} at {install.Executable}");
            if (!string.IsNullOrEmpty(install.Library))
            {
                Log.LogMessage(MessageImportance.High, $" -> libpython: {install.Library}");
            }

            Directory.CreateDirectory(targetEnvPath);

            foreach (string packageSpec in packagesToInstall)
            {
                Log.LogMessage(MessageImportance.High, $" -> Resolving PythonPackageReference: {packageSpec}");
            }

            if (packagesToInstall.Count > 0)
            {
                InstallPackagesViaPip(install.Executable, targetEnvPath, packagesToInstall);
            }

            StageNativeLibrary(install.Library, requested);
            GenerateDependencyManifest(targetEnvPath, install);

            Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Python Environment Restored Successfully ===");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }

    private List<string> CollectPackageSpecs()
    {
        List<string> packages = new();
        foreach (var item in PackageReferences ?? Array.Empty<ITaskItem>())
        {
            string packageName = item.ItemSpec;
            string version = item.GetMetadata("Version");
            packages.Add(string.IsNullOrWhiteSpace(version)
                ? packageName
                : $"{packageName}=={version}");
        }

        return packages;
    }

    private bool IsEnvironmentCurrent(
        PythonVersionSpec requested,
        List<string> packages,
        string targetEnvPath,
        out PythonInstall? install)
    {
        install = null;
        string manifestPath = Path.Combine(OutputDirectory, "python-deps.json");
        string sitePackages = Path.Combine(targetEnvPath, "site-packages");
        if (!File.Exists(manifestPath) || !Directory.Exists(sitePackages))
        {
            return false;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = doc.RootElement;

        string recordedVersion = root.TryGetProperty("PythonVersion", out JsonElement verProp)
            ? verProp.GetString() ?? string.Empty
            : string.Empty;

        if (!PythonVersionSpec.TryParse(recordedVersion, out PythonVersionSpec recorded) || recorded != requested)
            return false;

        if (!string.IsNullOrWhiteSpace(PythonExecutable)
            && File.Exists(PythonExecutable)
            && root.TryGetProperty("PythonExecutable", out JsonElement exeProp)
            && !string.Equals(exeProp.GetString(), PythonExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string spec in packages)
        {
            if (!IsPackageInstalled(sitePackages, spec))
                return false;
        }

        if (!HasNumpyMajor1(sitePackages))
            return false;

        string executable = root.TryGetProperty("PythonExecutable", out JsonElement exec)
            ? exec.GetString() ?? string.Empty
            : string.Empty;
        string prefix = root.TryGetProperty("PythonHome", out JsonElement home)
            ? home.GetString() ?? string.Empty
            : string.Empty;
        string library = root.TryGetProperty("PythonLibrary", out JsonElement lib)
            ? lib.GetString() ?? string.Empty
            : string.Empty;

        bool libraryAvailable = (!string.IsNullOrWhiteSpace(library) && File.Exists(library))
            || HasStagedLibrary(requested);

        if (!libraryAvailable)
            return false;

        install = new PythonInstall(executable, prefix, library, recordedVersion);
        return true;
    }

    private bool HasStagedLibrary(PythonVersionSpec spec)
    {
        foreach (string name in spec.StagedLibraryFileNames())
        {
            if (File.Exists(Path.Combine(OutputDirectory, name)))
                return true;
        }

        return false;
    }

    private static bool IsPackageInstalled(string sitePackages, string spec)
    {
        SplitSpec(spec, out string name, out string version);
        foreach (string folder in DistInfoFolderNames(name, version))
        {
            if (Directory.Exists(Path.Combine(sitePackages, folder)))
                return true;
        }

        if (string.IsNullOrEmpty(version))
        {
            foreach (string dir in Directory.EnumerateDirectories(sitePackages, "*.dist-info"))
            {
                string folder = Path.GetFileName(dir) ?? string.Empty;
                foreach (string prefix in PackageNameVariants(name))
                {
                    if (folder.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
                        && folder.EndsWith(".dist-info", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasNumpyMajor1(string sitePackages)
    {
        foreach (string dir in Directory.EnumerateDirectories(sitePackages, "numpy-*.dist-info"))
        {
            string folder = Path.GetFileName(dir) ?? string.Empty;
            if (folder.StartsWith("numpy-1.", StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith("numpy-0.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void SplitSpec(string spec, out string name, out string version)
    {
        int eq = spec.IndexOf("==", StringComparison.Ordinal);
        if (eq < 0)
        {
            name = spec;
            version = string.Empty;
            return;
        }

        name = spec[..eq];
        version = spec[(eq + 2)..];
    }

    private static IEnumerable<string> DistInfoFolderNames(string name, string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            yield break;
        }

        foreach (string variant in PackageNameVariants(name))
        {
            yield return $"{variant}-{version}.dist-info";
        }
    }

    private static IEnumerable<string> PackageNameVariants(string name)
    {
        yield return name;
        yield return name.Replace('-', '_');
        yield return name.Replace('_', '-');
    }

    private static bool IsTrue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private string ResolvePythonExecutable(PythonVersionSpec requested)
    {
        if (!string.IsNullOrWhiteSpace(PythonExecutable) && File.Exists(PythonExecutable))
            return PythonExecutable;

        string? fromEnv = Environment.GetEnvironmentVariable("DOTNETPIP_PYTHON");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        List<string> names = [.. requested.InterpreterNames()];

        if (OperatingSystem.IsWindows())
            names.AddRange(["py", "python", "python3"]);
        else
            names.AddRange(["python3", "python"]);

        foreach (string name in names)
        {
            string? found = FindOnPath(name);
            if (found != null)
            {
                return found;
            }
        }

        foreach (string path in requested.WellKnownExecutables())
        {
            if (File.Exists(path))
                return path;
        }

        throw new InvalidOperationException(
            $"Could not find a Python {requested.Dotted} interpreter. Install Python {requested.Dotted}, or set DOTNETPIP_PYTHON.");
    }

    private static string? FindOnPath(string name)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : [""];

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            foreach (string ext in extensions)
            {
                string candidate = Path.Combine(dir, name + ext);

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private PythonInstall ProbePython(string pythonExe)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), "dotnetpip_probe.py");
        File.WriteAllText(scriptPath,
            """
            import os, sys, sysconfig
            ver = "%d.%d" % sys.version_info[:2]
            prefix = sys.prefix
            libdir = sysconfig.get_config_var("LIBDIR") or ""
            ld = sysconfig.get_config_var("LDLIBRARY") or ""
            cands = [
                os.path.join(prefix, "Python3"),
                os.path.join(prefix, "Python"),
                os.path.join(prefix, "lib", "libpython%s.dylib" % ver),
                os.path.join(prefix, "lib", "libpython%s.so" % ver),
                os.path.join(prefix, "python%d%d.dll" % sys.version_info[:2]),
            ]
            if libdir and ld:
                cands.append(os.path.join(libdir, os.path.basename(ld)))
                cands.append(os.path.join(libdir, ld))
            lib = next((c for c in cands if c and os.path.exists(c)), "")
            print(sys.executable)
            print(prefix)
            print(lib)
            print(ver)
            """);

        try
        {
            string output = RunProcess(pythonExe, "\"" + scriptPath + "\"");
            string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 4)
            {
                throw new InvalidOperationException($"Could not probe Python installation at '{pythonExe}'. Output:\n{output}");
            }

            return new PythonInstall(lines[0].Trim(), lines[1].Trim(), lines[2].Trim(), lines[3].Trim());
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private void InstallPackagesViaPip(string pythonExe, string envPath, List<string> packages)
    {
        string sitePackagesDir = Path.Combine(envPath, "site-packages");
        Directory.CreateDirectory(sitePackagesDir);

        string constraintPath = Path.Combine(envPath, "constraints.txt");
        File.WriteAllText(constraintPath, "numpy<2" + Environment.NewLine);

        string args = "-m pip install --disable-pip-version-check --constraint \"" + constraintPath
            + "\" --target \"" + sitePackagesDir + "\" "
            + string.Join(" ", packages.Select(QuotePipSpec));
        string output = RunProcess(pythonExe, args);
        Log.LogMessage(MessageImportance.Normal, output);

        foreach (string leftover in Directory.GetDirectories(sitePackagesDir, "numpy*"))
        {
            Directory.Delete(leftover, recursive: true);
        }

        string pinNumpy = "-m pip install --disable-pip-version-check --target \""
            + sitePackagesDir + "\" \"numpy<2\"";
        string numpyOutput = RunProcess(pythonExe, pinNumpy);
        Log.LogMessage(MessageImportance.Normal, numpyOutput);
    }

    private static string QuotePipSpec(string spec)
    {
        return spec.Contains('<') || spec.Contains('>') || spec.Contains(' ')
            ? "\"" + spec + "\""
            : spec;
    }

    private void StageNativeLibrary(string libraryPath, PythonVersionSpec spec)
    {
        if (string.IsNullOrWhiteSpace(libraryPath) || !File.Exists(libraryPath))
            return;

        Directory.CreateDirectory(OutputDirectory);

        foreach (string name in spec.StagedLibraryFileNames())
        {
            string destination = Path.Combine(OutputDirectory, name);
            TryLinkOrCopy(libraryPath, destination);
        }
    }

    private void TryLinkOrCopy(string source, string destination)
    {
        try
        {
            if (File.Exists(destination) || Directory.Exists(destination))
                File.Delete(destination);

            File.CreateSymbolicLink(destination, source);
        }
        catch
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    private string RunProcess(string fileName, string arguments)
    {
        ProcessStartInfo psi = new()
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to launch '{fileName}'.");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {arguments}' failed with code {process.ExitCode}:\n{error}");
        }

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private void GenerateDependencyManifest(string envPath, PythonInstall install)
    {
        string manifestPath = Path.Combine(OutputDirectory, "python-deps.json");
        var manifestData = new
        {
            EnvironmentPath = envPath,
            PythonExecutable = install.Executable,
            PythonHome = install.Prefix,
            PythonLibrary = install.Library,
            PythonVersion = install.Version,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            Packages = (PackageReferences ?? Array.Empty<ITaskItem>()).Select(p => new
            {
                Name = p.ItemSpec,
                Version = p.GetMetadata("Version")
            })
        };

        string json = JsonSerializer.Serialize(manifestData, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    private sealed record PythonInstall(string Executable, string Prefix, string Library, string Version);
}