using System.Runtime.InteropServices;

namespace NothingVpn.Tray.Internal.SingBox;

internal static class WintunNative
{
    private const string DllName = "wintun.dll";
    private static readonly object Gate = new();
    private static nint _module;
    private static bool _resolved;
    private static bool _available;

    private delegate bool EnumAdaptersCallback(nint adapter, nint param);
    private delegate nint OpenAdapterDelegate([MarshalAs(UnmanagedType.LPWStr)] string name);
    private delegate void CloseAdapterDelegate(nint adapter);
    private delegate bool DeleteAdapterDelegate(nint adapter);
    private delegate bool EnumAdaptersDelegate(EnumAdaptersCallback callback, nint param);
    private delegate bool GetAdapterNameDelegate(nint adapter, [Out] char[] name);

    private static OpenAdapterDelegate? _openAdapter;
    private static CloseAdapterDelegate? _closeAdapter;
    private static DeleteAdapterDelegate? _deleteAdapter;
    private static EnumAdaptersDelegate? _enumAdapters;
    private static GetAdapterNameDelegate? _getAdapterName;

    private static readonly EnumAdaptersCallback EnumCallbackStatic = OnEnumAdapter;
    private static List<string>? _enumBuffer;

    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _available;
        }
    }

    public static bool TryDeleteAdapter(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName) || !EnsureLoaded())
            return false;

        var handle = _openAdapter!.Invoke(adapterName);
        if (handle == 0)
            return false;

        try
        {
            return _deleteAdapter!.Invoke(handle);
        }
        finally
        {
            _closeAdapter!.Invoke(handle);
        }
    }

    public static IReadOnlyList<string> ListAdapterNames()
    {
        if (!EnsureLoaded())
            return Array.Empty<string>();

        _enumBuffer = new List<string>();
        _enumAdapters!(EnumCallbackStatic, 0);
        var result = _enumBuffer.ToList();
        _enumBuffer = null;
        return result;
    }

    private static bool OnEnumAdapter(nint adapter, nint param)
    {
        var buffer = new char[256];
        if (_getAdapterName!(adapter, buffer))
        {
            var name = new string(buffer).TrimEnd('\0');
            if (name.Length > 0)
                _enumBuffer!.Add(name);
        }

        return true;
    }

    private static bool EnsureLoaded()
    {
        lock (Gate)
        {
            if (_resolved)
                return _available;

            _resolved = true;
            var dllPath = ResolveDllPath();
            if (dllPath is null)
                return false;

            if (!NativeLibrary.TryLoad(dllPath, out _module))
                return false;

            if (!TryResolveExports())
            {
                NativeLibrary.Free(_module);
                _module = 0;
                return false;
            }

            _available = true;
            return true;
        }
    }

    private static string? ResolveDllPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, DllName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool TryResolveExports()
    {
        if (!NativeLibrary.TryGetExport(_module, "WintunOpenAdapter", out var open))
            return false;
        if (!NativeLibrary.TryGetExport(_module, "WintunCloseAdapter", out var close))
            return false;
        if (!NativeLibrary.TryGetExport(_module, "WintunDeleteAdapter", out var delete))
            return false;
        if (!NativeLibrary.TryGetExport(_module, "WintunEnumAdapters", out var enumerate))
            return false;
        if (!NativeLibrary.TryGetExport(_module, "WintunGetAdapterName", out var getName))
            return false;

        _openAdapter = Marshal.GetDelegateForFunctionPointer<OpenAdapterDelegate>(open);
        _closeAdapter = Marshal.GetDelegateForFunctionPointer<CloseAdapterDelegate>(close);
        _deleteAdapter = Marshal.GetDelegateForFunctionPointer<DeleteAdapterDelegate>(delete);
        _enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdaptersDelegate>(enumerate);
        _getAdapterName = Marshal.GetDelegateForFunctionPointer<GetAdapterNameDelegate>(getName);
        return true;
    }
}
