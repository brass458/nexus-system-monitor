using System.Diagnostics;
using Microsoft.Win32;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Platform.Windows;

/// <summary>
/// Reads Windows startup programs from:
///   • HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
///   • HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
///   • HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run
///   • Per-user and all-users Startup folders
/// Enable/disable state is persisted via the StartupApproved registry keys.
/// </summary>
public sealed class WindowsStartupProvider : IStartupProvider
{
    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<StartupItem>>(Enumerate, ct);

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default)
        => Task.Run(() => SetEnabled(item, enabled), ct);

    // ─── Enumerate ────────────────────────────────────────────────────────────

    private static List<StartupItem> Enumerate()
    {
        var list = new List<StartupItem>();

        // HKCU\Run
        EnumerateRunKey(
            Registry.CurrentUser,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            StartupItemType.RegistryCurrentUser, "HKCU\\Run", list);

        // HKLM\Run  (64-bit)
        EnumerateRunKey(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            StartupItemType.RegistryLocalMachine, "HKLM\\Run", list);

        // HKLM\Run  (32-bit WOW64)
        EnumerateRunKey(
            Registry.LocalMachine,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            StartupItemType.RegistryLocalMachine, "HKLM\\Run (32-bit)", list);

        // Startup folders
        EnumerateFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Startup Folder (User)", list);

        EnumerateFolder(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            "Startup Folder (All Users)", list);

        return list;
    }

    private static void EnumerateRunKey(
        RegistryKey hive,
        string runPath,
        string approvedPath,
        StartupItemType itemType,
        string locationLabel,
        List<StartupItem> list)
    {
        try
        {
            using var runKey = hive.OpenSubKey(runPath);
            if (runKey is null) return;

            var approved = ReadApproved(hive, approvedPath);

            foreach (string valueName in runKey.GetValueNames())
            {
                string command = (runKey.GetValue(valueName) as string) ?? string.Empty;
                bool isEnabled = approved.TryGetValue(valueName, out bool e) ? e : true;

                list.Add(new StartupItem
                {
                    Name      = valueName,
                    Command   = command,
                    Publisher = GetPublisher(command),
                    Location  = locationLabel,
                    IsEnabled = isEnabled,
                    ItemType  = itemType,
                });
            }
        }
        catch { /* access denied */ }
    }

    /// <summary>
    /// Reads the StartupApproved binary values.
    /// First byte of value: 0x02 = disabled, anything else = enabled.
    /// </summary>
    private static Dictionary<string, bool> ReadApproved(RegistryKey hive, string approvedPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = hive.OpenSubKey(approvedPath);
            if (key is null) return result;
            foreach (string name in key.GetValueNames())
            {
                if (key.GetValue(name) is byte[] bytes && bytes.Length > 0)
                    result[name] = bytes[0] != 0x02;
            }
        }
        catch { }
        return result;
    }

    private static void EnumerateFolder(string path, string location, List<StartupItem> list)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (string file in Directory.EnumerateFiles(path))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".lnk" && ext != ".exe" && ext != ".bat" && ext != ".cmd") continue;

                list.Add(new StartupItem
                {
                    Name      = Path.GetFileNameWithoutExtension(file),
                    Command   = file,
                    Publisher = GetPublisher(file),
                    Location  = location,
                    IsEnabled = true,           // folder items can't be disabled via registry
                    ItemType  = StartupItemType.StartupFolder,
                });
            }
        }
        catch { }
    }

    // ─── Publisher lookup ─────────────────────────────────────────────────────

    private static string GetPublisher(string command)
    {
        string? exePath = ParseExePath(command.Trim());
        if (exePath is null || !File.Exists(exePath)) return string.Empty;
        try
        {
            return FileVersionInfo.GetVersionInfo(exePath).CompanyName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string? ParseExePath(string cmd)
    {
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            return end > 0 ? cmd[1..end] : null;
        }
        int space = cmd.IndexOf(' ');
        return space > 0 ? cmd[..space] : cmd;
    }

    // ─── Enable / Disable ─────────────────────────────────────────────────────

    private static void SetEnabled(StartupItem item, bool enabled)
    {
        (RegistryKey hive, string approvedPath) = item.ItemType switch
        {
            StartupItemType.RegistryCurrentUser  =>
                (Registry.CurrentUser,
                 @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            StartupItemType.RegistryLocalMachine =>
                (Registry.LocalMachine,
                 @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            _ => (Registry.CurrentUser, string.Empty),     // folder items: no-op
        };

        if (string.IsNullOrEmpty(approvedPath)) return;

        try
        {
            using var key = hive.CreateSubKey(approvedPath, writable: true);
            if (key is null) return;

            // 8-byte binary: first byte 02 = disabled, 00 = enabled
            var bytes = new byte[8];
            bytes[0] = enabled ? (byte)0x00 : (byte)0x02;
            key.SetValue(item.Name, bytes, RegistryValueKind.Binary);
        }
        catch { }
    }
}
