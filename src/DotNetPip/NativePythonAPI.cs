using System.Reflection;
using System.Runtime.InteropServices;

namespace DotNetPip.Native;

internal static partial class NativePythonAPI
{
    private const string PythonLib = "python";
    private static IntPtr _libraryHandle;
    private static string? _explicitLibraryPath;
    private static PythonVersionSpec? _version;

    internal static void Load(string? explicitLibraryPath = null, string? pythonVersion = null)
    {
        if (_libraryHandle != IntPtr.Zero)
        {
            return;
        }

        _explicitLibraryPath = explicitLibraryPath;
        if (PythonVersionSpec.TryParse(pythonVersion, out PythonVersionSpec spec))
        {
            _version = spec;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativePythonAPI).Assembly, Resolve);
        _libraryHandle = LoadLibrary(explicitLibraryPath);
        if (_libraryHandle == IntPtr.Zero)
        {
            string dotted = _version?.Dotted ?? "3.x";
            throw new DllNotFoundException(
                $"Could not load libpython {dotted}. Install Python {dotted}, or set DOTNETPIP_PYTHON_LIB to the CPython shared library.");
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != PythonLib)
        {
            return IntPtr.Zero;
        }

        if (_libraryHandle == IntPtr.Zero)
        {
            _libraryHandle = LoadLibrary(_explicitLibraryPath);
        }

        return _libraryHandle;
    }

    private static IntPtr LoadLibrary(string? explicitLibraryPath)
    {
        foreach (string candidate in EnumerateCandidates(explicitLibraryPath))
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string[] EnumerateCandidates(string? explicitLibraryPath)
    {
        List<string> candidates = [];

        if (!string.IsNullOrWhiteSpace(explicitLibraryPath))
        {
            candidates.Add(explicitLibraryPath);
        }

        string? fromEnv = Environment.GetEnvironmentVariable("DOTNETPIP_PYTHON_LIB");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            candidates.Add(fromEnv);
        }

        if (_version is PythonVersionSpec spec)
        {
            candidates.AddRange(spec.LibrarySearchPaths(AppContext.BaseDirectory));
        }

        return [.. candidates];
    }

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_Initialize();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_Finalize();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Py_IsInitialized();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PyRun_SimpleString([MarshalAs(UnmanagedType.LPUTF8Str)] string code);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyImport_ImportModule([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_GetAttrString(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PyObject_SetAttrString(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string attrName, IntPtr value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_CallObject(IntPtr obj, IntPtr args);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_Str(IntPtr obj);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyUnicode_FromString([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyUnicode_AsUTF8(IntPtr unicode);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyLong_FromLongLong(long value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyFloat_FromDouble(double value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyBool_FromLong(int value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyTuple_New(nint len);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PyTuple_SetItem(IntPtr tuple, nint pos, IntPtr item);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Py_BuildValue([MarshalAs(UnmanagedType.LPUTF8Str)] string format);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_IncRef(IntPtr o);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_DecRef(IntPtr o);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyErr_Occurred();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PyErr_Fetch(out IntPtr pType, out IntPtr pValue, out IntPtr pTraceback);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PyErr_Clear();

    [StructLayout(LayoutKind.Sequential)]
    public struct Py_buffer
    {
        public IntPtr buf;
        public IntPtr obj;
        public nint len;
        public nint itemsize;
        public int readonly_flag;
        public int ndim;
        public IntPtr format;
        public IntPtr shape;
        public IntPtr strides;
        public IntPtr suboffsets;
        public IntPtr internal_ptr;
    }

    public const int PyBUF_SIMPLE = 0;
    public const int PyBUF_WRITABLE = 0x0001;

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PyObject_GetBuffer(IntPtr exporter, out Py_buffer view, int flags);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PyBuffer_Release(ref Py_buffer view);

    public static string? AsManagedString(IntPtr pyObject)
    {
        if (pyObject == IntPtr.Zero)
        {
            return null;
        }

        IntPtr str = PyObject_Str(pyObject);
        if (str == IntPtr.Zero)
        {
            PyErr_Clear();
            return null;
        }

        try
        {
            IntPtr utf8 = PyUnicode_AsUTF8(str);
            return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        }
        finally
        {
            Py_DecRef(str);
        }
    }
}
