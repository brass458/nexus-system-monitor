using System.Runtime.InteropServices;
using NexusMonitor.Core.Gaming;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Windows implementation of <see cref="IPowerPlanProvider"/> using PowrProf.dll.
/// </summary>
public sealed class WindowsPowerPlanProvider : IPowerPlanProvider
{
    // ── P/Invoke declarations ─────────────────────────────────────────────────

    private const uint ERROR_NO_MORE_ITEMS = 259;
    private const uint ACCESS_SCHEME       = 16;

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerEnumerate(
        IntPtr RootPowerKey,
        IntPtr SchemeGuid,
        IntPtr SubGroupOfPowerSetting,
        uint   AccessFlags,
        uint   Index,
        IntPtr Buffer,
        ref uint BufferSize);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerGetActiveScheme(
        IntPtr   UserRootPowerKey,
        out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerSetActiveScheme(
        IntPtr UserRootPowerKey,
        ref Guid SchemeGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern uint PowerReadFriendlyName(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        IntPtr SubGroupOfPowerSetting,
        IntPtr PowerSettingGuid,
        IntPtr Buffer,
        ref uint BufferSize);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    // ── IPowerPlanProvider ────────────────────────────────────────────────────

    public IReadOnlyList<PowerPlanInfo> GetPowerPlans()
    {
        var plans  = new List<PowerPlanInfo>();
        Guid active;
        try { active = GetActivePlan(); } catch { active = Guid.Empty; }

        uint index = 0;
        while (true)
        {
            uint bufSize = (uint)Marshal.SizeOf<Guid>();
            IntPtr buf   = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                uint result = PowerEnumerate(
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    ACCESS_SCHEME, index, buf, ref bufSize);

                if (result == ERROR_NO_MORE_ITEMS) break;
                if (result != 0) break; // unexpected error

                Guid guid = Marshal.PtrToStructure<Guid>(buf);
                string name = GetFriendlyName(guid);
                bool isActive = guid == active;
                plans.Add(new PowerPlanInfo(guid, name, isActive));
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
            index++;
        }

        return plans;
    }

    public Guid GetActivePlan()
    {
        uint result = PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid);
        if (result != 0)
            throw new InvalidOperationException($"PowerGetActiveScheme failed: {result}");

        Guid guid = Marshal.PtrToStructure<Guid>(pGuid);
        LocalFree(pGuid);
        return guid;
    }

    public void SetActivePlan(Guid schemeGuid)
    {
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
        if (result != 0)
            throw new InvalidOperationException($"PowerSetActiveScheme failed: {result}");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string GetFriendlyName(Guid schemeGuid)
    {
        try
        {
            uint bufSize = 0;
            PowerReadFriendlyName(
                IntPtr.Zero, ref schemeGuid,
                IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, ref bufSize);

            if (bufSize == 0) return schemeGuid.ToString();

            IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
            try
            {
                uint result = PowerReadFriendlyName(
                    IntPtr.Zero, ref schemeGuid,
                    IntPtr.Zero, IntPtr.Zero,
                    buf, ref bufSize);

                if (result != 0) return schemeGuid.ToString();

                return Marshal.PtrToStringUni(buf) ?? schemeGuid.ToString();
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        catch
        {
            return schemeGuid.ToString();
        }
    }
}
