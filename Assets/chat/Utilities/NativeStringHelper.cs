// Native string helper (C#)
public static class NativeStringHelper
{
    [DllImport("__Internal")]
    private static extern void freeNativeString(IntPtr s);

    public static string PtrToStringAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        string s = Marshal.PtrToStringAnsi(ptr);
        freeNativeString(ptr);
        return s;
    }
}
