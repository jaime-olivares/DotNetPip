using System.Text.Json;
using DotNetPip.Native;

namespace DotNetPip;

public sealed class PythonEngine : IDisposable
{
    private static readonly Lazy<PythonEngine> _instance = new(() => new PythonEngine());
    private bool _initialized;
    private bool _disposed;

    public static PythonEngine Instance => _instance.Value;

    private PythonEngine() { }

    public static PythonEngine CreateMinimalist(string? environmentPath = null)
    {
        var engine = Instance;
        engine.Initialize(environmentPath);
        return engine;
    }

    private void Initialize(string? customEnvPath)
    {
        if (_initialized)
        {
            return;
        }

        PythonRuntimePaths paths = DiscoverRuntimePaths(customEnvPath);
        NativePythonAPI.Load(paths.Library, paths.Version);

        if (!string.IsNullOrWhiteSpace(paths.Home))
        {
            Environment.SetEnvironmentVariable("PYTHONHOME", paths.Home, EnvironmentVariableTarget.Process);
        }

        if (!string.IsNullOrWhiteSpace(paths.SitePackages))
        {
            string existing = Environment.GetEnvironmentVariable("PYTHONPATH") ?? string.Empty;
            string combined = string.IsNullOrWhiteSpace(existing)
                ? paths.SitePackages
                : paths.SitePackages + Path.PathSeparator + existing;
            Environment.SetEnvironmentVariable("PYTHONPATH", combined, EnvironmentVariableTarget.Process);
        }

        NativePythonAPI.Py_Initialize();
        if (NativePythonAPI.Py_IsInitialized() == 0)
        {
            throw new InvalidOperationException("Failed to initialize CPython embedded engine.");
        }

        if (!string.IsNullOrWhiteSpace(paths.SitePackages))
        {
            string escaped = JsonSerializer.Serialize(paths.SitePackages);
            NativePythonAPI.PyRun_SimpleString(
                "import sys; p=" + escaped + "; sys.path.insert(0, p) if p not in sys.path else None");
        }

        _initialized = true;
    }

    private static PythonRuntimePaths DiscoverRuntimePaths(string? customEnvPath)
    {
        string baseDir = AppContext.BaseDirectory;
        string manifestPath = Path.Combine(baseDir, "python-deps.json");
        string envPath = customEnvPath ?? Path.Combine(baseDir, "python_env");
        string? library = null;
        string? home = null;
        string? version = null;

        if (File.Exists(manifestPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = doc.RootElement;
            if (customEnvPath == null && root.TryGetProperty("EnvironmentPath", out var pathProp))
            {
                envPath = pathProp.GetString() ?? envPath;
            }
            if (root.TryGetProperty("PythonLibrary", out var libProp))
            {
                library = libProp.GetString();
            }
            if (root.TryGetProperty("PythonHome", out var homeProp))
            {
                home = homeProp.GetString();
            }
            if (root.TryGetProperty("PythonVersion", out var verProp))
            {
                version = verProp.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(library) && PythonVersionSpec.TryParse(version, out PythonVersionSpec spec))
        {
            foreach (string candidate in spec.StagedLibraryFileNames().Select(name => Path.Combine(baseDir, name)))
            {
                if (File.Exists(candidate))
                {
                    library = candidate;
                    break;
                }
            }
        }

        return new PythonRuntimePaths(envPath, Path.Combine(envPath, "site-packages"), library, home, version);
    }

    public dynamic Import(string moduleName)
    {
        EnsureInitialized();
        IntPtr pModule = NativePythonAPI.PyImport_ImportModule(moduleName);

        CheckAndThrowPythonException();

        if (pModule == IntPtr.Zero)
        {
            throw new PythonInvocationException($"Could not import module '{moduleName}'.");
        }

        return new PyObjectWrapper(pModule);
    }

    internal static void CheckAndThrowPythonException()
    {
        IntPtr err = NativePythonAPI.PyErr_Occurred();
        if (err == IntPtr.Zero)
        {
            return;
        }

        NativePythonAPI.PyErr_Fetch(out IntPtr pType, out IntPtr pValue, out IntPtr pTraceback);
        NativePythonAPI.PyErr_Clear();

        string message = NativePythonAPI.AsManagedString(pValue)
            ?? NativePythonAPI.AsManagedString(pType)
            ?? "Python interpreter error.";

        if (pType != IntPtr.Zero) NativePythonAPI.Py_DecRef(pType);
        if (pValue != IntPtr.Zero) NativePythonAPI.Py_DecRef(pValue);
        if (pTraceback != IntPtr.Zero) NativePythonAPI.Py_DecRef(pTraceback);

        throw new PythonInvocationException(message);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("PythonEngine must be initialized before importing modules.");
        }
    }

    public void Dispose()
    {
        if (!_disposed && _initialized)
        {
            NativePythonAPI.Py_Finalize();
            _initialized = false;
            _disposed = true;
        }
    }

    private sealed record PythonRuntimePaths(string EnvironmentPath, string SitePackages, string? Library, string? Home, string? Version);
}

public class PythonInvocationException : Exception
{
    public PythonInvocationException(string message) : base(message) { }
    public PythonInvocationException(string message, Exception inner) : base(message, inner) { }
}
