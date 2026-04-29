namespace NothingVpn.Tray.Internal.UI;

internal sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
    }
}
