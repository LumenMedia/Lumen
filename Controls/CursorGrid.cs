using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Lumen.Controls;

public sealed class CursorGrid : Grid
{
    private InputCursor? _visibleCursor;

    public CursorGrid()
    {
    }

    public void ShowPointer()
    {
        _visibleCursor?.Dispose();
        _visibleCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursor = _visibleCursor;
    }

    public void HidePointer()
    {
        // In WinUI, ProtectedCursor controls the client-area pointer. Disposing
        // the active InputCursor leaves the XAML element without a drawable
        // pointer until a fresh cursor is assigned.
        _visibleCursor?.Dispose();
        _visibleCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursor = _visibleCursor;
        _visibleCursor.Dispose();
        _visibleCursor = null;
    }
}
