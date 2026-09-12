using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using System.Reflection;

namespace Lumen.Controls;

public static class CursorBehavior
{
    private static readonly MethodInfo? ProtectedCursorSetter =
        typeof(UIElement).GetMethod(
            "set_ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Dictionary<UIElement, InputCursor> VisibleCursors = new();

    public static void Hide(UIElement? element)
    {
        if (element is null || ProtectedCursorSetter is null)
            return;

        ReleaseVisibleCursor(element);

        // WinUI has no Hidden system cursor. Assign an Arrow cursor through the
        // protected setter and immediately dispose it; Windows App SDK users use
        // this to suppress the client-area pointer without subclassing the XAML control.
        var cursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursorSetter.Invoke(element, new object?[] { cursor });
        cursor.Dispose();
    }

    public static void Show(UIElement? element)
    {
        if (element is null || ProtectedCursorSetter is null)
            return;

        ReleaseVisibleCursor(element);

        var cursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursorSetter.Invoke(element, new object?[] { cursor });
        VisibleCursors[element] = cursor;
    }

    private static void ReleaseVisibleCursor(UIElement element)
    {
        if (!VisibleCursors.Remove(element, out var old))
            return;

        old.Dispose();
    }
}
