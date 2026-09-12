using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Lumen.Controls;

public sealed class CursorMediaPlayerElement : MediaPlayerElement
{
    private InputCursor? _cursor;

    public CursorMediaPlayerElement()
    {
    }

    public void ShowPointer()
    {
        _cursor?.Dispose();
        _cursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursor = _cursor;
    }

    public void HidePointer()
    {
        _cursor?.Dispose();

        // WinUI has no Hidden InputSystemCursorShape. Assigning a cursor and then
        // disposing it is the established WinUI workaround for suppressing the
        // client-area pointer on a specific XAML element.
        _cursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        ProtectedCursor = _cursor;
        _cursor.Dispose();
        _cursor = null;
    }
}
