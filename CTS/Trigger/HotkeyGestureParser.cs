using CircleToSearch.Interop;

namespace CircleToSearch.Trigger;

// canonical display order is Win+Ctrl+Alt+Shift followed by the key, matching Windows
// conventions ("Win+Shift+A") and keeping the default gesture as "Ctrl+Alt+Space".
public static class HotkeyGestureParser
{
    private static readonly Dictionary<string, uint> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = NativeMethods.MOD_CONTROL,
        ["Control"] = NativeMethods.MOD_CONTROL,
        ["Alt"] = NativeMethods.MOD_ALT,
        ["Shift"] = NativeMethods.MOD_SHIFT,
        ["Win"] = NativeMethods.MOD_WIN,
        ["Windows"] = NativeMethods.MOD_WIN,
    };

    private static readonly Dictionary<string, uint> Keys = BuildKeys();

    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length < 2) return false;

        var parsedModifiers = 0u;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!Modifiers.TryGetValue(tokens[i], out var modifier)) return false;
            if ((parsedModifiers & modifier) != 0) return false;
            parsedModifiers |= modifier;
        }
        if (parsedModifiers == 0) return false;

        if (!Keys.TryGetValue(tokens[^1], out var key)) return false;

        modifiers = parsedModifiers;
        virtualKey = key;
        return true;
    }

    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(virtualKey));
        return string.Join('+', parts);
    }

    private static string KeyName(uint virtualKey)
    {
        foreach (var (name, key) in Keys)
        {
            if (key == virtualKey) return name;
        }
        return $"0x{virtualKey:X2}";
    }

    private static Dictionary<string, uint> BuildKeys()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { ["Space"] = 0x20 };
        for (var letter = 'A'; letter <= 'Z'; letter++)
            keys[letter.ToString()] = (uint)(0x41 + letter - 'A');
        for (var digit = '0'; digit <= '9'; digit++)
            keys[digit.ToString()] = (uint)(0x30 + digit - '0');
        for (var index = 1; index <= 12; index++)
            keys[$"F{index}"] = (uint)(0x70 + index - 1);
        keys["Insert"] = 0x2D;
        keys["Delete"] = 0x2E;
        keys["Home"] = 0x24;
        keys["End"] = 0x23;
        keys["PageUp"] = 0x21;
        keys["PageDown"] = 0x22;
        return keys;
    }
}
