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

    public string LockFilePath { get; set; } = string.Empty;

    public string RestoreLockedMode { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            if (!PythonVersionSpec.TryParse(PythonVersion, out PythonVersionSpec requested))
            {
                throw new InvalidOperationException(
                    "Set <PythonRuntimeVersion> in the .csproj (for example 3.11.5).");
            }

            List<PythonLockDirect> directs = CollectDirects();
            List<string> constraints = PythonLockFile.DefaultConstraints();
            (string? indexUrl, List<string> extraIndexUrls) = PythonLockFile.IndexesFromEnvironment();
            string targetEnvPath = Path.Combine(OutputDirectory, "python_env");
            string lockPath = ResolveLockFilePath();
            bool force = IsTrue(ForceRestore);
            bool lockedMode = IsTrue(RestoreLockedMode);

            PythonLockFile? lockFile = force ? null : PythonLockFile.TryRead(lockPath);
            bool lockMatches = lockFile is not null
                && lockFile.Matches(requested.Dotted, directs, constraints, indexUrl, extraIndexUrls);

            if (!force && lockMatches && IsEnvironmentCurrent(requested, lockFile!, targetEnvPath, out PythonInstall? cached))
            {
                StageNativeLibrary(cached!.Library, requested);
                Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Python environment is up to date ===");
                return true;
            }

            if (!lockMatches)
            {
                if (lockedMode)
                {
                    throw new InvalidOperationException(
                        "Python restore is in locked mode but obj/python.lock.json is missing or does not match the project. "
                        + "Run a local build without RestoreLockedMode to refresh the lock, then retry.");
                }

                Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Restoring Python Dependencies ===");
                foreach (PythonLockDirect direct in directs)
                {
                    string spec = string.IsNullOrWhiteSpace(direct.Version)
                        ? direct.Name
                        : $"{direct.Name}=={direct.Version}";
                    Log.LogMessage(MessageImportance.High, $" -> Resolving PythonPackageReference: {spec}");
                }

                PythonInstall resolveInstall = ProbeMatchingPython(requested);
                lockFile = directs.Count == 0
                    ? new PythonLockFile
                    {
                        Python = requested.Dotted,
                        IndexUrl = indexUrl,
                        ExtraIndexUrls = extraIndexUrls,
                        Constraints = constraints,
                        Direct = directs,
                    }
                    : ResolveLockFile(resolveInstall.Executable, requested.Dotted, directs, constraints, indexUrl, extraIndexUrls);
                lockFile.Write(lockPath);
                Log.LogMessage(MessageImportance.High, $" -> Wrote lock with {lockFile.Packages.Count} packages to {lockPath}");
            }
            else
            {
                Log.LogMessage(MessageImportance.High, "=== [DotNetPip] Restoring Python Dependencies from lock ===");
            }

            PythonInstall install = ProbeMatchingPython(requested);
            Log.LogMessage(MessageImportance.High, $" -> Using Python {install.Version} at {install.Executable}");
            if (!string.IsNullOrEmpty(install.Library))
            {
                Log.LogMessage(MessageImportance.High, $" -> libpython: {install.Library}");
            }

            Directory.CreateDirectory(targetEnvPath);

            if (lockFile!.Packages.Count > 0
                && !LockPackagesInstalled(lockFile, Path.Combine(targetEnvPath, "site-packages")))
            {
                InstallFromLock(install.Executable, targetEnvPath, lockFile);
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

    private PythonInstall ProbeMatchingPython(PythonVersionSpec requested)
    {
        string pythonExe = ResolvePythonExecutable(requested);
        PythonInstall install = ProbePython(pythonExe);
        if (!PythonVersionSpec.TryParse(install.Version, out PythonVersionSpec actual)
            || actual != requested)
        {
            throw new InvalidOperationException(
                $"PythonRuntimeVersion is {requested.Dotted} but the interpreter at '{install.Executable}' is {install.Version}. Install Python {requested.Dotted} or set DOTNETPIP_PYTHON.");
        }

        return install;
    }

    private string ResolveLockFilePath()
    {
        if (!string.IsNullOrWhiteSpace(LockFilePath))
            return LockFilePath;

        return Path.Combine(OutputDirectory, "python.lock.json");
    }

    private List<PythonLockDirect> CollectDirects()
    {
        List<PythonLockDirect> directs = [];
        foreach (var item in PackageReferences ?? Array.Empty<ITaskItem>())
        {
            directs.Add(new PythonLockDirect
            {
                Name = item.ItemSpec,
                Version = item.GetMetadata("Version") ?? string.Empty,
            });
        }

        return directs;
    }

    private PythonLockFile ResolveLockFile(
        string pythonExe,
        string python,
        List<PythonLockDirect> directs,
        List<string> constraints,
        string? indexUrl,
        List<string> extraIndexUrls)
    {
        string workDir = Path.Combine(Path.GetTempPath(), "dotnetpip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(workDir);
        string reportPath = Path.Combine(workDir, "report.json");
        string constraintPath = Path.Combine(workDir, "constraints.txt");
        File.WriteAllLines(constraintPath, constraints);

        List<string> specs = directs.Select(d =>
            string.IsNullOrWhiteSpace(d.Version) ? d.Name : $"{d.Name}=={d.Version}").ToList();

        string args = "-m pip install --disable-pip-version-check --dry-run --ignore-installed --report \""
            + reportPath + "\" --constraint \"" + constraintPath + "\" "
            + string.Join(" ", specs.Select(QuotePipSpec));

        try
        {
            string output = RunProcess(pythonExe, args);
            Log.LogMessage(MessageImportance.Normal, output);
            if (!File.Exists(reportPath))
            {
                throw new InvalidOperationException(
                    "pip --report did not write a report. Upgrade pip to 22.2 or later (python -m pip install -U pip).");
            }

            return PythonLockFile.FromPipReport(
                File.ReadAllText(reportPath),
                python,
                directs,
                constraints,
                indexUrl,
                extraIndexUrls);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private void InstallFromLock(string pythonExe, string envPath, PythonLockFile lockFile)
    {
        string sitePackagesDir = Path.Combine(envPath, "site-packages");
        if (Directory.Exists(sitePackagesDir))
        {
            Directory.Delete(sitePackagesDir, recursive: true);
        }

        Directory.CreateDirectory(sitePackagesDir);

        string workDir = Path.Combine(Path.GetTempPath(), "dotnetpip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(workDir);
        string requirementsPath = Path.Combine(workDir, "requirements.txt");
        File.WriteAllText(requirementsPath, lockFile.ToHashRequirements());

        string args = "-m pip install --disable-pip-version-check --require-hashes --no-deps --target \""
            + sitePackagesDir + "\" -r \"" + requirementsPath + "\"";
        try
        {
            string output = RunProcess(pythonExe, args);
            Log.LogMessage(MessageImportance.Normal, output);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private bool IsEnvironmentCurrent(
        PythonVersionSpec requested,
        PythonLockFile lockFile,
        string targetEnvPath,
        out PythonInstall? install)
    {
        install = null;
        string manifestPath = Path.Combine(OutputDirectory, "python-deps.json");
        string sitePackages = Path.Combine(targetEnvPath, "site-packages");
        if (!File.Exists(manifestPath) || !Directory.Exists(sitePackages))
            return false;

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
            return false;

        if (!LockPackagesInstalled(lockFile, sitePackages))
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

    private static bool LockPackagesInstalled(PythonLockFile lockFile, string sitePackages)
    {
        if (!Directory.Exists(sitePackages))
            return false;

        foreach (PythonLockPackage package in lockFile.Packages)
        {
            if (!IsPackageInstalled(sitePackages, package.Name, package.Version))
                return false;
        }

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

    private static bool IsPackageInstalled(string sitePackages, string name, string version)
    {
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
                        return true;
                }
            }
        }

        return false;
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
                return found;
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

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string output = outputTask.GetAwaiter().GetResult();
        string error = errorTask.GetAwaiter().GetResult();

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
