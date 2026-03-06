using System.Runtime.InteropServices;
using System.Text;

namespace NexusMonitor.Platform.Windows.Shell;

// ── COM interface declarations (vtable layout must match COM exactly) ─────────

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214E6-0000-0000-C000-000000000046")]
internal interface IShellFolder
{
    void ParseDisplayName(nint hwnd, nint pbc,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        ref uint pchEaten, out nint ppidl, ref uint pdwAttributes);
    void EnumObjects(nint hwnd, uint grfFlags, out nint ppenumIDList);
    void BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    void BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);
    [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
    void CreateViewObject(nint hwnd, ref Guid riid, out nint ppv);
    void GetAttributesOf(uint cidl, nint apidl, ref uint rgfInOut);
    [PreserveSig]
    int GetUIObjectOf(nint hwndOwner, uint cidl,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
        ref Guid riid, nint rgfReserved, out nint ppv);
    void GetDisplayNameOf(nint pidl, uint uFlags, nint pName);
    void SetNameOf(nint hwnd, nint pidl,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        uint uFlags, out nint ppidlOut);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214E4-0000-0000-C000-000000000046")]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig]
    int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
    [PreserveSig]
    int GetCommandString(nint idCmd, uint uType, nint pReserved,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, uint cchMax);
}

// IContextMenu2 vtable: IContextMenu methods + HandleMenuMsg (flat, no C# inheritance)
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F4-0000-0000-C000-000000000046")]
internal interface IContextMenu2
{
    [PreserveSig]
    int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig]
    int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
    [PreserveSig]
    int GetCommandString(nint idCmd, uint uType, nint pReserved,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, uint cchMax);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
}

// IContextMenu3 vtable: IContextMenu + IContextMenu2 + HandleMenuMsg2 (flat)
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
internal interface IContextMenu3
{
    [PreserveSig]
    int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig]
    int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
    [PreserveSig]
    int GetCommandString(nint idCmd, uint uType, nint pReserved,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, uint cchMax);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
    [PreserveSig]
    int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
}

// ── Struct ────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct CMINVOKECOMMANDINFO
{
    public uint cbSize;
    public uint fMask;
    public nint hwnd;
    public nint lpVerb;         // LPCSTR: use (nint)offset for numeric ID
    public nint lpParameters;
    public nint lpDirectory;
    public int  nShow;
    public uint dwHotKey;
    public nint hIcon;
}

// ── P/Invoke declarations ──────────────────────────────────────────────────────

internal static class NativeShell32
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(
        string pszName, nint pbc, out nint ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern int SHBindToParent(
        nint pidl, ref Guid riid, out nint ppv, out nint ppidlLast);
}

internal static class NativeMenuUser32
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenuEx(
        nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);
}

internal static class NativeComctl32
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate nint SUBCLASSPROC(
        nint hWnd, uint uMsg, nint wParam, nint lParam,
        nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(
        nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(
        nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(
        nint hWnd, uint uMsg, nint wParam, nint lParam);
}

internal static class NativeOle32
{
    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(nint pv);
}
