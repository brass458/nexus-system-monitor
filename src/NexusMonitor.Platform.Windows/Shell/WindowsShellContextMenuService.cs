using System.Runtime.InteropServices;
using NexusMonitor.Core.Abstractions;

namespace NexusMonitor.Platform.Windows.Shell;

/// <summary>
/// Shows the real Windows Explorer shell context menu for a file or folder.
/// Supports owner-drawn items (7-Zip, antivirus, etc.) via IContextMenu2/3 + window subclassing.
/// </summary>
public sealed class WindowsShellContextMenuService : IShellContextMenuService
{
    public bool IsSupported => true;

    // ── Static subclass state ─────────────────────────────────────────────────
    // TrackPopupMenuEx is blocking/synchronous, so statics are safe (one menu open at a time).

    private static IContextMenu2? _activeCm2;
    private static IContextMenu3? _activeCm3;

    // Static delegate to prevent GC collection; identity is stable across calls.
    private static readonly NativeComctl32.SUBCLASSPROC _subclassProc = MenuSubclassProc;

    private const nuint SubclassId  = 0x4E584D43u; // "NXMC" as uint

    private const uint WM_DRAWITEM      = 0x002B;
    private const uint WM_MEASUREITEM   = 0x002C;
    private const uint WM_INITMENUPOPUP = 0x0117;

    private static nint MenuSubclassProc(
        nint hWnd, uint uMsg, nint wParam, nint lParam,
        nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg is WM_DRAWITEM or WM_MEASUREITEM or WM_INITMENUPOPUP)
        {
            if (_activeCm3 is { } cm3)
            {
                cm3.HandleMenuMsg2(uMsg, wParam, lParam, out nint result);
                return result;
            }
            if (_activeCm2 is { } cm2)
            {
                cm2.HandleMenuMsg(uMsg, wParam, lParam);
                return nint.Zero;
            }
        }
        return NativeComctl32.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ShowContextMenu(string filePath, nint windowHandle)
    {
        nint          pidl           = nint.Zero;
        nint          parentFolderPv = nint.Zero;
        nint          contextMenuPv  = nint.Zero;
        nint          hmenu          = nint.Zero;
        IShellFolder? parentFolder   = null;
        IContextMenu? contextMenu    = null;
        bool          subclassSet    = false;

        try
        {
            // 1. Path → PIDL
            int hr = NativeShell32.SHParseDisplayName(filePath, nint.Zero, out pidl, 0, out _);
            if (hr < 0 || pidl == nint.Zero) return;

            // 2. PIDL → parent IShellFolder + child relative PIDL
            var iidShellFolder = typeof(IShellFolder).GUID;
            hr = NativeShell32.SHBindToParent(pidl, ref iidShellFolder, out parentFolderPv, out nint childPidl);
            if (hr < 0 || parentFolderPv == nint.Zero) return;
            parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPv);
            Marshal.Release(parentFolderPv); // parentFolder now holds the ref

            // 3. IShellFolder.GetUIObjectOf → IContextMenu
            var    iidContextMenu = typeof(IContextMenu).GUID;
            nint[] childPidls     = [childPidl]; // childPidl is interior pointer into pidl — do not free
            hr = parentFolder.GetUIObjectOf(windowHandle, 1, childPidls, ref iidContextMenu,
                nint.Zero, out contextMenuPv);
            if (hr < 0 || contextMenuPv == nint.Zero) return;
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPv);

            // 4. Create popup menu
            hmenu = NativeMenuUser32.CreatePopupMenu();
            if (hmenu == nint.Zero) return;

            // 5. Populate the menu
            const uint CMF_NORMAL  = 0x00000000;
            const uint CMF_EXPLORE = 0x00000004;
            const uint IdFirst     = 1;
            const uint IdLast      = 0x7FFF;

            hr = contextMenu.QueryContextMenu(hmenu, 0, IdFirst, IdLast, CMF_NORMAL | CMF_EXPLORE);
            if (hr < 0) return;

            // 6. Query for IContextMenu2/3 (needed for owner-drawn items, e.g. 7-Zip)
            var iidCm3 = typeof(IContextMenu3).GUID;
            var iidCm2 = typeof(IContextMenu2).GUID;

            if (Marshal.QueryInterface(contextMenuPv, ref iidCm3, out nint cm3Pv) >= 0 && cm3Pv != nint.Zero)
            {
                _activeCm3 = (IContextMenu3)Marshal.GetObjectForIUnknown(cm3Pv);
                Marshal.Release(cm3Pv);
            }
            else if (Marshal.QueryInterface(contextMenuPv, ref iidCm2, out nint cm2Pv) >= 0 && cm2Pv != nint.Zero)
            {
                _activeCm2 = (IContextMenu2)Marshal.GetObjectForIUnknown(cm2Pv);
                Marshal.Release(cm2Pv);
            }

            // 7. Install window subclass to forward owner-draw messages to IContextMenu2/3
            subclassSet = NativeComctl32.SetWindowSubclass(windowHandle, _subclassProc, SubclassId, 0);

            // 8. Show the menu at the current cursor position (blocking — runs its own message pump)
            NativeMenuUser32.GetCursorPos(out var cursorPos);
            const uint TPM_RETURNCMD   = 0x0100;
            const uint TPM_RIGHTBUTTON = 0x0002;
            uint cmdId = NativeMenuUser32.TrackPopupMenuEx(
                hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursorPos.X, cursorPos.Y, windowHandle, nint.Zero);

            // 9. Remove subclass
            if (subclassSet)
                NativeComctl32.RemoveWindowSubclass(windowHandle, _subclassProc, SubclassId);
            subclassSet = false;

            // 10. Execute selected command
            if (cmdId >= IdFirst)
            {
                var ici = new CMINVOKECOMMANDINFO
                {
                    cbSize       = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    fMask        = 0,
                    hwnd         = windowHandle,
                    lpVerb       = (nint)(cmdId - IdFirst), // MAKEINTRESOURCE(offset)
                    lpParameters = nint.Zero,
                    lpDirectory  = nint.Zero,
                    nShow        = 1, // SW_SHOWNORMAL
                };
                contextMenu.InvokeCommand(ref ici);
            }
        }
        catch
        {
            // Swallow — never let a context menu failure crash the app
        }
        finally
        {
            if (subclassSet)
                NativeComctl32.RemoveWindowSubclass(windowHandle, _subclassProc, SubclassId);

            if (_activeCm3 is { } cm3) { Marshal.ReleaseComObject(cm3); _activeCm3 = null; }
            if (_activeCm2 is { } cm2) { Marshal.ReleaseComObject(cm2); _activeCm2 = null; }
            if (contextMenu is not null)  Marshal.ReleaseComObject(contextMenu);
            if (parentFolder is not null) Marshal.ReleaseComObject(parentFolder);
            if (hmenu  != nint.Zero) NativeMenuUser32.DestroyMenu(hmenu);
            if (pidl   != nint.Zero) NativeOle32.CoTaskMemFree(pidl);
            // contextMenuPv was AddRef'd by GetObjectForIUnknown → handled by contextMenu wrapper
        }
    }
}
