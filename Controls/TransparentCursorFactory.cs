using Microsoft.UI.Input;
using System.Runtime.InteropServices;

namespace Lumen.Controls;

public sealed class TransparentCursorHandle : IDisposable
{
    private nint _hCursor;

    public InputCursor? Cursor { get; }
    public nint NativeHandle => _hCursor;

    private TransparentCursorHandle(nint hCursor, InputCursor? cursor)
    {
        _hCursor = hCursor;
        Cursor = cursor;
    }

    public static TransparentCursorHandle Create()
    {
        // A monochrome cursor is transparent where AND=1 and XOR=0.
        // 32x32 => 128 bytes per mask.
        var andMask = Enumerable.Repeat((byte)0xFF, 128).ToArray();
        var xorMask = new byte[128];

        var hCursor = CreateCursor(
            nint.Zero,
            0,
            0,
            32,
            32,
            andMask,
            xorMask);

        if (hCursor == nint.Zero)
            return new TransparentCursorHandle(nint.Zero, null);

        var cursor = CreateInputCursorFromHCursor(hCursor);
        return new TransparentCursorHandle(hCursor, cursor);
    }

    public void ApplyNative()
    {
        if (_hCursor != nint.Zero)
            SetCursor(_hCursor);
    }

    public static void RestoreSystemArrow()
    {
        var arrow = LoadCursor(nint.Zero, (nint)32512); // IDC_ARROW
        if (arrow != nint.Zero)
            SetCursor(arrow);
    }

    public void Dispose()
    {
        Cursor?.Dispose();

        if (_hCursor != nint.Zero)
        {
            DestroyCursor(_hCursor);
            _hCursor = nint.Zero;
        }
    }

    private static InputCursor? CreateInputCursorFromHCursor(nint hCursor)
    {
        if (hCursor == nint.Zero)
            return null;

        const string classId = "Microsoft.UI.Input.InputCursor";

        var hr = WindowsCreateString(classId, classId.Length, out var classString);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            hr = RoGetActivationFactory(
                classString,
                typeof(IActivationFactory).GUID,
                out var factory);
            Marshal.ThrowExceptionForHR(hr);

            if (factory is not IInputCursorStaticsInterop interop)
                return null;

            hr = interop.CreateFromHCursor(hCursor, out var cursorAbi);
            Marshal.ThrowExceptionForHR(hr);

            if (cursorAbi == nint.Zero)
                return null;

            return WinRT.MarshalInspectable<InputCursor>.FromAbi(cursorAbi);
        }
        finally
        {
            WindowsDeleteString(classString);
        }
    }

    [ComImport]
    [Guid("AC6F5065-90C4-46CE-BEB7-05E138E54117")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInputCursorStaticsInterop
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();

        [PreserveSig]
        int CreateFromHCursor(nint hCursor, out nint inputCursor);
    }

    [ComImport]
    [Guid("00000035-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationFactory
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();

        [PreserveSig]
        int ActivateInstance(out nint instance);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateCursor(
        nint hInst,
        int xHotSpot,
        int yHotSpot,
        int nWidth,
        int nHeight,
        byte[] pvANDPlane,
        byte[] pvXORPlane);

    [DllImport("user32.dll")]
    private static extern bool DestroyCursor(nint hCursor);

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursor(nint hInstance, nint lpCursorName);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static extern int RoGetActivationFactory(
        nint runtimeClassId,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        out IActivationFactory factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(
        string? sourceString,
        int length,
        out nint hString);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static extern int WindowsDeleteString(nint hString);
}
