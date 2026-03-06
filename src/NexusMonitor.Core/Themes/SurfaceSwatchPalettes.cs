using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.Themes;

public static class SurfaceSwatchPalettes
{
    private static readonly SwatchColor[] DefaultDark =
    [
        new("#0A84FF", "Blue"),
        new("#BF5AF2", "Purple"),
        new("#FF375F", "Pink"),
        new("#FF9F0A", "Orange"),
        new("#FFD60A", "Yellow"),
        new("#30D158", "Green"),
        new("#5AC8FA", "Teal"),
        new("#FF453A", "Red"),
    ];

    private static readonly SwatchColor[] DefaultLight =
    [
        new("#007AFF", "Blue"),
        new("#AF52DE", "Purple"),
        new("#E84393", "Pink"),
        new("#E8590C", "Orange"),
        new("#D4A017", "Amber"),
        new("#28A745", "Green"),
        new("#0288D1", "Teal"),
        new("#DC3545", "Red"),
    ];

    private static readonly Dictionary<string, SwatchColor[]> _palettes = new()
    {
        ["neon"] =
        [
            new("#39FF14", "Neon Green"),
            new("#FF00FF", "Magenta"),
            new("#00FFFF", "Cyan"),
            new("#FF6600", "Neon Orange"),
            new("#FFFF00", "Electric Yellow"),
            new("#FF0066", "Hot Pink"),
            new("#00FF99", "Mint"),
            new("#9D00FF", "Violet"),
        ],
        ["dark-sakura"] =
        [
            new("#FF6B9D", "Sakura"),
            new("#FFB0CC", "Petal"),
            new("#E84393", "Cherry"),
            new("#FF8DC7", "Bubblegum"),
            new("#D4A5FF", "Wisteria"),
            new("#FF9EBB", "Rose"),
            new("#FFD4E5", "Blush"),
            new("#C084FC", "Lavender"),
        ],
        ["anime"] =
        [
            new("#FF6B81", "Coral"),
            new("#FFD93D", "Gold"),
            new("#FF4F81", "Hot Pink"),
            new("#6C5CE7", "Indigo"),
            new("#00CEC9", "Turquoise"),
            new("#FD79A8", "Flamingo"),
            new("#A29BFE", "Soft Purple"),
            new("#55EFC4", "Mint"),
        ],
        ["futuristic"] =
        [
            new("#00D4FF", "Cyan"),
            new("#00FFE0", "Aqua"),
            new("#48CAE4", "Sky"),
            new("#00B4D8", "Ocean"),
            new("#90E0EF", "Ice"),
            new("#0077B6", "Deep Blue"),
            new("#00FFAA", "Matrix"),
            new("#ADE8F4", "Frost"),
        ],
        ["outer-space"] =
        [
            new("#7C5CFC", "Nebula"),
            new("#B8A9FF", "Stardust"),
            new("#E040FB", "Plasma"),
            new("#BB86FC", "Cosmic"),
            new("#6366F1", "Indigo"),
            new("#A78BFA", "Amethyst"),
            new("#818CF8", "Iris"),
            new("#C084FC", "Orchid"),
        ],
        ["magical"] =
        [
            new("#E040FB", "Mystic"),
            new("#FFD740", "Gold"),
            new("#FF6EFF", "Fairy"),
            new("#BB86FC", "Spell"),
            new("#FF80AB", "Charm"),
            new("#FFAB40", "Amber"),
            new("#EA80FC", "Orchid"),
            new("#B388FF", "Crystal"),
        ],
        ["techno"] =
        [
            new("#00FF9C", "Terminal"),
            new("#CCFF00", "Lime"),
            new("#39FF14", "Neon"),
            new("#00E676", "Bright Green"),
            new("#76FF03", "Chartreuse"),
            new("#B2FF59", "Light Green"),
            new("#00FFAA", "Mint"),
            new("#AEEA00", "Yellow-Green"),
        ],
        ["ocean-depth"] =
        [
            new("#00BCD4", "Reef"),
            new("#4DD0E1", "Shallow"),
            new("#00ACC1", "Lagoon"),
            new("#26C6DA", "Surf"),
            new("#80DEEA", "Foam"),
            new("#00E5FF", "Electric"),
            new("#18FFFF", "Neon Aqua"),
            new("#84FFFF", "Ice"),
        ],
        ["sunset"] =
        [
            new("#FF7043", "Tangerine"),
            new("#FFB74D", "Amber"),
            new("#FF8A65", "Peach"),
            new("#FFAB91", "Salmon"),
            new("#FF5722", "Flame"),
            new("#FFA726", "Marigold"),
            new("#FFCC80", "Apricot"),
            new("#FF6E40", "Burnt Orange"),
        ],
        ["dracula"] =
        [
            new("#BD93F9", "Purple"),
            new("#FF79C6", "Pink"),
            new("#50FA7B", "Green"),
            new("#F1FA8C", "Yellow"),
            new("#8BE9FD", "Cyan"),
            new("#FFB86C", "Orange"),
            new("#FF5555", "Red"),
            new("#6272A4", "Comment"),
        ],
        ["solarized-dark"] =
        [
            new("#268BD2", "Blue"),
            new("#2AA198", "Cyan"),
            new("#859900", "Green"),
            new("#B58900", "Yellow"),
            new("#CB4B16", "Orange"),
            new("#DC322F", "Red"),
            new("#D33682", "Magenta"),
            new("#6C71C4", "Violet"),
        ],
        ["solarized-light"] =
        [
            new("#268BD2", "Blue"),
            new("#2AA198", "Cyan"),
            new("#859900", "Green"),
            new("#B58900", "Yellow"),
            new("#CB4B16", "Orange"),
            new("#DC322F", "Red"),
            new("#D33682", "Magenta"),
            new("#6C71C4", "Violet"),
        ],
        ["nord"] =
        [
            new("#88C0D0", "Frost"),
            new("#81A1C1", "Blue"),
            new("#5E81AC", "Dark Blue"),
            new("#BF616A", "Red"),
            new("#D08770", "Orange"),
            new("#EBCB8B", "Yellow"),
            new("#A3BE8C", "Green"),
            new("#B48EAD", "Purple"),
        ],
        ["cherry-blossom"] =
        [
            new("#E84393", "Cherry"),
            new("#D63384", "Dark Cherry"),
            new("#FF6B9D", "Sakura"),
            new("#FF85B3", "Rose"),
            new("#C084FC", "Lavender"),
            new("#FF9EBB", "Blush"),
            new("#FD79A8", "Flamingo"),
            new("#A855F7", "Violet"),
        ],
        ["arctic"] =
        [
            new("#0288D1", "Ocean"),
            new("#0277BD", "Deep"),
            new("#039BE5", "Sky"),
            new("#0288D1", "Steel"),
            new("#00ACC1", "Teal"),
            new("#00BCD4", "Cyan"),
            new("#4FC3F7", "Ice"),
            new("#29B6F6", "Azure"),
        ],
    };

    public static SwatchColor[] GetPalette(string presetId, bool isDark)
    {
        if (!string.IsNullOrEmpty(presetId) && _palettes.TryGetValue(presetId, out var palette))
            return palette;

        return isDark ? DefaultDark : DefaultLight;
    }
}
