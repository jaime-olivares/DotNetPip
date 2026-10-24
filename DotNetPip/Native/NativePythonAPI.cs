using System.Runtime.InteropServices;

namespace DotNetPip.Native;

internal static partial class NativePythonAPI
{
    private const string PythonLib = "python311"; // Dynamically resolved at runtime based on OS/Version

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_Initialize();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_Finalize();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Py_IsInitialized();

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyImport_ImportModule([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_GetAttrString(IntPtr obj, [MarshalAs(UnmanagedType.LPStr)] string attrName);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PyObject_SetAttrString(IntPtr obj, [MarshalAs(UnmanagedType.LPStr)] string attrName, IntPtr value);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_CallObject(IntPtr obj, IntPtr args);

    [DllImport(PythonLib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyObject_CallFunctionObjArgs(IntPtr callable, params IntPtr[] args);

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

    // Buffer Protocol API for Zero-Copy Memory Access
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
}
