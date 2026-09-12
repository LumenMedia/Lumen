using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Lumen.Controls;

public sealed class PlayerSurfaceGrid : Grid
{
    public InputCursor? InputCursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
