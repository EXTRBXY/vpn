using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NothingVpn.Tray.Internal.UI;

/// <summary>
/// Если курсор над вложенным контролом, а тот не использует вертикальный скролл,
/// колёсико уходит в ближайший AutoScroll-контейнер (страница не «залипает»).
/// Закрытый ComboBox / NumericUpDown значение колёсиком не меняют.
/// </summary>
internal static class ParentScrollWheelRelay
{
    private const int WmMouseWheel = 0x020A;
    private const int SbVert = 1;
    private const uint SifAll = 0x17;

    private static readonly ConditionalWeakTable<Form, FormFilter> Filters = new();

    public static void Install(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Filters.TryGetValue(form, out _))
            return;

        var filter = new FormFilter(form);
        Filters.Add(form, filter);
        System.Windows.Forms.Application.AddMessageFilter(filter);
        form.FormClosed += (_, _) => System.Windows.Forms.Application.RemoveMessageFilter(filter);
    }

    private sealed class FormFilter : IMessageFilter
    {
        private readonly Form _form;

        public FormFilter(Form form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel || !_form.IsHandleCreated)
                return false;

            var hit = Control.FromHandle(m.HWnd) ?? Control.FromChildHandle(m.HWnd);
            if (hit is null || !BelongsToForm(hit, _form))
                return false;

            var scrollParent = FindAutoScrollParent(hit);
            if (scrollParent is null || ReferenceEquals(hit, scrollParent))
                return false;

            var delta = GetWheelDelta(m.WParam);
            for (var c = hit; c != null && !ReferenceEquals(c, scrollParent); c = c.Parent)
            {
                if (WantsVerticalWheel(c, delta))
                    return false;
            }

            if (!scrollParent.IsHandleCreated)
                return false;

            SendMessage(scrollParent.Handle, m.Msg, m.WParam, m.LParam);
            return true;
        }
    }

    private static bool BelongsToForm(Control control, Form form)
    {
        for (var c = control; c != null; c = c.Parent)
        {
            if (ReferenceEquals(c, form))
                return true;
        }

        return false;
    }

    private static ScrollableControl? FindAutoScrollParent(Control start)
    {
        for (var p = start.Parent; p != null; p = p.Parent)
        {
            if (p is ScrollableControl { AutoScroll: true } sc)
                return sc;
        }

        return null;
    }

    private static bool WantsVerticalWheel(Control control, int delta)
    {
        switch (control)
        {
            case ComboBox { DroppedDown: true }:
                return true;
            case ComboBox:
            case NumericUpDown:
            case TrackBar:
                return false;
            case TextBox { Multiline: false }:
                return false;
            case TextBox textBox:
                return CanScrollVertically(textBox.Handle, delta);
            case RichTextBox richTextBox:
                return CanScrollVertically(richTextBox.Handle, delta);
            case DataGridView dataGridView:
                return CanScrollDataGridView(dataGridView, delta);
            case ListView listView:
                return CanScrollVertically(listView.Handle, delta);
            case TreeView treeView:
                return CanScrollVertically(treeView.Handle, delta);
            case ScrollableControl { AutoScroll: true } sc:
                return CanScrollVertically(sc.Handle, delta);
            default:
                return false;
        }
    }

    private static bool CanScrollDataGridView(DataGridView grid, int delta)
    {
        if (grid.Rows.Count == 0)
            return false;

        try
        {
            var first = grid.FirstDisplayedScrollingRowIndex;
            if (first < 0)
                return false;

            var displayed = grid.DisplayedRowCount(includePartialRow: false);
            if (displayed <= 0)
                return false;

            if (delta > 0)
                return first > 0;

            return first + displayed < grid.Rows.Count;
        }
        catch
        {
            return CanScrollVertically(grid.Handle, delta);
        }
    }

    private static bool CanScrollVertically(IntPtr hwnd, int delta)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var info = new ScrollInfo
        {
            CbSize = (uint)Marshal.SizeOf<ScrollInfo>(),
            FMask = SifAll
        };
        if (!GetScrollInfo(hwnd, SbVert, ref info))
            return false;

        var maxPos = Math.Max(0, info.NMax - (int)info.NPage + 1);
        if (maxPos <= info.NMin)
            return false;

        if (delta > 0)
            return info.NPos > info.NMin;

        return info.NPos < maxPos;
    }

    private static int GetWheelDelta(IntPtr wParam)
        => (short)((wParam.ToInt64() >> 16) & 0xFFFF);

    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint CbSize;
        public uint FMask;
        public int NMin;
        public int NMax;
        public uint NPage;
        public int NPos;
        public int NTrackPos;
    }

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
