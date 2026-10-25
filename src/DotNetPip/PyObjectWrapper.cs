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
        {
            throw new ArgumentNullException(nameof(pyObject));
        }

        _pyObject = pyObject;
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

        try
        {
            result = Invoke(attr, args);
            return result != null;
        }
        finally
        {
            NativePythonAPI.Py_DecRef(attr);
        }
    }

    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        result = Invoke(_pyObject, args);
        return result != null;
    }

    private static PyObjectWrapper? Invoke(IntPtr callable, object?[]? args)
    {
        IntPtr tuple = BuildArgTuple(args);
        try
        {
            IntPtr pyResult = NativePythonAPI.PyObject_CallObject(callable, tuple);
            PythonEngine.CheckAndThrowPythonException();
            return pyResult == IntPtr.Zero ? null : new PyObjectWrapper(pyResult);
        }
        finally
        {
            NativePythonAPI.Py_DecRef(tuple);
        }
    }

    private static IntPtr BuildArgTuple(object?[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return NativePythonAPI.PyTuple_New(0);
        }

        IntPtr tuple = NativePythonAPI.PyTuple_New(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            IntPtr value = ToPyObject(args[i]);
            if (NativePythonAPI.PyTuple_SetItem(tuple, i, value) != 0)
            {
                NativePythonAPI.Py_DecRef(tuple);
                PythonEngine.CheckAndThrowPythonException();
                throw new PythonInvocationException("Failed to build Python argument tuple.");
            }
        }

        return tuple;
    }

    private static IntPtr ToPyObject(object? obj)
    {
        switch (obj)
        {
            case null:
                return NativePythonAPI.Py_BuildValue("");
            case PyObjectWrapper wrapper:
                NativePythonAPI.Py_IncRef(wrapper.NativePointer);
                return wrapper.NativePointer;
            case string s:
                return NativePythonAPI.PyUnicode_FromString(s);
            case char c:
                return NativePythonAPI.PyUnicode_FromString(c.ToString());
            case bool b:
                return NativePythonAPI.PyBool_FromLong(b ? 1 : 0);
            case byte or sbyte or short or ushort or int or uint or long:
                return NativePythonAPI.PyLong_FromLongLong(Convert.ToInt64(obj));
            case float or double or decimal:
                return NativePythonAPI.PyFloat_FromDouble(Convert.ToDouble(obj));
            default:
                throw new PythonInvocationException($"Cannot convert CLR type '{obj.GetType().FullName}' to a Python object.");
        }
    }

    public override string ToString()
    {
        return NativePythonAPI.AsManagedString(_pyObject) ?? $"[PyObject 0x{_pyObject:X}]";
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
