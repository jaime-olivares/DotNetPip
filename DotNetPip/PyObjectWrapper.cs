using System.Dynamic;
using DotNetPip.Native;

namespace DotNetPip;

public class PyObjectWrapper : DynamicObject, IDisposable
{
    private IntPtr _pyObject;
    private bool _disposed;

    public IntPtr NativePointer => _pyObject;

    public PyObjectWrapper(IntPtr pyObject)
    {
        if (pyObject == IntPtr.Zero)
            throw new ArgumentNullException(nameof(pyObject));

        _pyObject = pyObject;
        NativePythonAPI.Py_IncRef(_pyObject);
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        IntPtr attr = NativePythonAPI.PyObject_GetAttrString(_pyObject, binder.Name);
        PythonEngine.CheckAndThrowPythonException();

        if (attr == IntPtr.Zero)
        {
            result = null;
            return false;
        }

        result = new PyObjectWrapper(attr);
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        IntPtr attr = NativePythonAPI.PyObject_GetAttrString(_pyObject, binder.Name);
        PythonEngine.CheckAndThrowPythonException();

        if (attr == IntPtr.Zero)
        {
            result = null;
            return false;
        }

        IntPtr[] pyArgs = ConvertToPyArgs(args);
        IntPtr pyResult = NativePythonAPI.PyObject_CallFunctionObjArgs(attr, pyArgs);
        
        foreach (var arg in pyArgs)
        {
            NativePythonAPI.Py_DecRef(arg);
        }
        NativePythonAPI.Py_DecRef(attr);

        PythonEngine.CheckAndThrowPythonException();

        if (pyResult == IntPtr.Zero)
        {
            result = null;
            return false;
        }

        result = new PyObjectWrapper(pyResult);
        return true;
    }

    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        IntPtr[] pyArgs = ConvertToPyArgs(args);
        IntPtr pyResult = NativePythonAPI.PyObject_CallFunctionObjArgs(_pyObject, pyArgs);

        foreach (var arg in pyArgs)
        {
            NativePythonAPI.Py_DecRef(arg);
        }

        PythonEngine.CheckAndThrowPythonException();

        if (pyResult == IntPtr.Zero)
        {
            result = null;
            return false;
        }

        result = new PyObjectWrapper(pyResult);
        return true;
    }

    private static IntPtr[] ConvertToPyArgs(object?[]? args)
    {
        if (args == null || args.Length == 0) return Array.Empty<IntPtr>();

        IntPtr[] pyArgs = new IntPtr[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is PyObjectWrapper wrapper)
            {
                pyArgs[i] = wrapper.NativePointer;
                NativePythonAPI.Py_IncRef(pyArgs[i]);
            }
            else
            {
                pyArgs[i] = wrapper_to_py(args[i]);
            }
        }
        return pyArgs;
    }

    private static IntPtr wrapper_to_py(object? obj)
    {
        return IntPtr.Zero;
    }

    public override string ToString()
    {
        return $"[PyObject 0x{_pyObject:X}]";
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (_pyObject != IntPtr.Zero)
            {
                NativePythonAPI.Py_DecRef(_pyObject);
                _pyObject = IntPtr.Zero;
            }
            _disposed = true;
        }
    }

    ~PyObjectWrapper()
    {
        Dispose(false);
    }
}
