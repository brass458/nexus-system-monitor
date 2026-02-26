using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Mock;

public class MockStartupProvider : IStartupProvider
{
    private readonly List<StartupItem> _items =
    [
        new() { Name = "Microsoft OneDrive",          Command = @"C:\Users\User\AppData\Local\Microsoft\OneDrive\OneDrive.exe /background",                  Publisher = "Microsoft Corporation", Location = "HKCU\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryCurrentUser  },
        new() { Name = "Spotify",                     Command = @"C:\Users\User\AppData\Roaming\Spotify\Spotify.exe --autostart",                            Publisher = "Spotify AB",            Location = "HKCU\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryCurrentUser  },
        new() { Name = "Discord",                     Command = @"C:\Users\User\AppData\Local\Discord\Update.exe --processStart Discord.exe",                 Publisher = "Discord Inc.",          Location = "HKCU\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryCurrentUser  },
        new() { Name = "Steam",                       Command = @"C:\Program Files (x86)\Steam\steam.exe -silent",                                            Publisher = "Valve Corporation",     Location = "HKCU\\Run",              IsEnabled = false, ItemType = StartupItemType.RegistryCurrentUser  },
        new() { Name = "SecurityHealthSystray",       Command = @"C:\Windows\System32\SecurityHealthSystray.exe",                                             Publisher = "Microsoft Windows",     Location = "HKLM\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryLocalMachine },
        new() { Name = "RTHDVCPL",                    Command = @"C:\Program Files\Realtek\Audio\HDA\RtkNGUI64.exe -s",                                       Publisher = "Realtek",               Location = "HKLM\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryLocalMachine },
        new() { Name = "NVIDIA GeForce Experience",   Command = @"C:\Program Files\NVIDIA Corporation\NVIDIA GeForce Experience\NVIDIA GeForce Experience.exe", Publisher = "NVIDIA Corporation",  Location = "HKLM\\Run",              IsEnabled = true,  ItemType = StartupItemType.RegistryLocalMachine },
        new() { Name = "NvBackend",                   Command = @"C:\Program Files (x86)\NVIDIA Corporation\Update Core\NvBackend.exe",                       Publisher = "NVIDIA Corporation",    Location = "HKLM\\Run (32-bit)",     IsEnabled = false, ItemType = StartupItemType.RegistryLocalMachine },
        new() { Name = "Razer Synapse",               Command = @"C:\Program Files (x86)\Razer\Synapse3\WPFUI\Framework\Razer Synapse 3 Host\Razer Synapse 3.exe", Publisher = "Razer Inc.",      Location = "Startup Folder (User)",  IsEnabled = true,  ItemType = StartupItemType.StartupFolder        },
    ];

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<StartupItem>>(_items);

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default)
    {
        int idx = _items.FindIndex(i => i.Name == item.Name && i.Location == item.Location);
        if (idx >= 0)
            _items[idx] = _items[idx] with { IsEnabled = enabled };
        return Task.CompletedTask;
    }
}
