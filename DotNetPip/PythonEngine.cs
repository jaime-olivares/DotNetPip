using System.Text.Json;

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
        if (_initialized) return;

        string envPath = customEnvPath ?? DiscoverEnvironmentPath();
        ConfigurePythonHome(envPath);

        Native.NativePythonAPI.Py_Initialize();
        if (Native.NativePythonAPI.Py_IsInitialized() == 0)
        {
            throw new InvalidOperationException("Failed to initialize CPython embedded engine.");
        }

        _initialized = true;
    }

    private static string DiscoverEnvironmentPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string manifestPath = Path.Combine(baseDir, "python-deps.json");

        if (File.Exists(manifestPath))
        {
            string json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("EnvironmentPath", out var pathProp))
            {
                return pathProp.GetString() ?? baseDir;
            }
        }

        return Path.Combine(baseDir, "python_env");
    }

    private static void ConfigurePythonHome(string envPath)
    {
        Environment.SetEnvironmentVariable("PYTHONHOME", envPath, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("PYTHONPATH", Path.Combine(envPath, "site-packages"), EnvironmentVariableTarget.Process);
    }

    public dynamic Import(string moduleName)
    {
        EnsureInitialized();
        IntPtr pModule = Native.NativePythonAPI.PyImport_ImportModule(moduleName);
        
        CheckAndThrowPythonException();

        if (pModule == IntPtr.Zero)
        {
            throw new PythonInvocationException($"Could not import module '{moduleName}'.");
        }

        return new PyObjectWrapper(pModule);
    }

    internal static void CheckAndThrowPythonException()
    {
        IntPtr err = Native.NativePythonAPI.PyErr_Occurred();
        if (err != IntPtr.Zero)
        {
            Native.NativePythonAPI.PyErr_Fetch(out IntPtr pType, out IntPtr pValue, out IntPtr pTraceback);
            Native.NativePythonAPI.PyErr_Clear();
            throw new PythonInvocationException($"Python Interpreter Error occurred: Type Pointer {pType}, Value Pointer {pValue}");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("PythonEngine must be initialized before importing modules.");
    }

    public void Dispose()
    {
        if (!_disposed && _initialized)
        {
            Native.NativePythonAPI.Py_Finalize();
            _initialized = false;
            _disposed = true;
        }
    }
}

public class PythonInvocationException : Exception
{
    public PythonInvocationException(string message) : base(message) { }
    public PythonInvocationException(string message, Exception inner) : base(message, inner) { }
}
