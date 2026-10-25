using DotNetPip.Native;

namespace DotNetPip;

public static class BufferProtocol
{
    public static unsafe Span<T> AsSpan<T>(PyObjectWrapper pyObject) where T : unmanaged
    {
        if (NativePythonAPI.PyObject_GetBuffer(pyObject.NativePointer, out NativePythonAPI.Py_buffer view, NativePythonAPI.PyBUF_WRITABLE) != 0)
        {
            NativePythonAPI.PyErr_Clear();
            if (NativePythonAPI.PyObject_GetBuffer(pyObject.NativePointer, out view, NativePythonAPI.PyBUF_SIMPLE) != 0)
            {
                throw new InvalidOperationException("Object does not support Python Buffer Protocol.");
            }
        }

        try
        {
            int totalElements = (int)(view.len / sizeof(T));
            return new Span<T>((void*)view.buf, totalElements);
        }
        finally
        {
            NativePythonAPI.PyBuffer_Release(ref view);
        }
    }
}
