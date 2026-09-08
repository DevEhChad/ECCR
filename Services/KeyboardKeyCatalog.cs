using System.Collections.Generic;
using Avalonia.Input;

namespace ECCR.Services;

/// <summary>
/// Single source of truth for the keyboard keys a "[Keyboard] ..." <c>MappingEntry.TargetOutput</c>
/// can name: the friendly name shown/searched in the Key Picker Flyout, and the Win32 virtual-key
/// code <see cref="KeyboardFeederService"/> needs to actually simulate it via <c>SendInput</c>.
/// Mirrors why <see cref="DevicePresetService"/> is the one place device-name vocabulary lives -
/// keeps the picker UI and the feeder from maintaining two separate key lists that could drift.
/// </summary>
public static class KeyboardKeyCatalog
{
    /// <summary>(FriendlyName, Win32 virtual-key code), in the order offered to the user.</summary>
    private static readonly List<(string Name, ushort Vk)> Entries = BuildEntries();

    public static IReadOnlyList<string> AllKeyNames { get; } = Entries.ConvertAll(e => e.Name);

    private static readonly Dictionary<string, ushort> NameToVk = BuildNameToVk();
    private static readonly Dictionary<Key, string> AvaloniaKeyToName = BuildAvaloniaKeyToName();

    public static bool TryGetVirtualKeyCode(string keyName, out ushort vk) => NameToVk.TryGetValue(keyName, out vk);

    /// <summary>Friendly name for an Avalonia <see cref="Key"/> pressed during "press a key" capture, or null if this key isn't in the catalog (e.g. a modifier-only or IME key).</summary>
    public static string? GetFriendlyName(Key key) => AvaloniaKeyToName.TryGetValue(key, out var name) ? name : null;

    private static Dictionary<string, ushort> BuildNameToVk()
    {
        var map = new Dictionary<string, ushort>();
        foreach (var (name, vk) in Entries) map[name] = vk;
        return map;
    }

    private static Dictionary<Key, string> BuildAvaloniaKeyToName()
    {
        var map = new Dictionary<Key, string>();
        void Add(Key key, string name) => map[key] = name;

        for (int i = 0; i <= 25; i++) Add((Key)((int)Key.A + i), ((char)('A' + i)).ToString());
        for (int i = 0; i <= 9; i++) Add((Key)((int)Key.D0 + i), i.ToString());
        for (int i = 1; i <= 24; i++) Add((Key)((int)Key.F1 + i - 1), $"F{i}");
        for (int i = 0; i <= 9; i++) Add((Key)((int)Key.NumPad0 + i), $"Numpad {i}");

        Add(Key.Up, "Up Arrow"); Add(Key.Down, "Down Arrow"); Add(Key.Left, "Left Arrow"); Add(Key.Right, "Right Arrow");
        Add(Key.Space, "Space"); Add(Key.Enter, "Enter"); Add(Key.Escape, "Escape"); Add(Key.Tab, "Tab");
        Add(Key.Back, "Backspace"); Add(Key.Delete, "Delete"); Add(Key.Insert, "Insert");
        Add(Key.Home, "Home"); Add(Key.End, "End"); Add(Key.PageUp, "Page Up"); Add(Key.PageDown, "Page Down");
        Add(Key.LeftShift, "Left Shift"); Add(Key.RightShift, "Right Shift");
        Add(Key.LeftCtrl, "Left Ctrl"); Add(Key.RightCtrl, "Right Ctrl");
        Add(Key.LeftAlt, "Left Alt"); Add(Key.RightAlt, "Right Alt");
        Add(Key.CapsLock, "Caps Lock"); Add(Key.NumLock, "Num Lock"); Add(Key.Scroll, "Scroll Lock");
        Add(Key.Multiply, "Numpad *"); Add(Key.Add, "Numpad +"); Add(Key.Subtract, "Numpad -");
        Add(Key.Decimal, "Numpad ."); Add(Key.Divide, "Numpad /");
        Add(Key.OemMinus, "-"); Add(Key.OemPlus, "="); Add(Key.OemComma, ","); Add(Key.OemPeriod, ".");
        Add(Key.OemQuestion, "/"); Add(Key.OemSemicolon, ";"); Add(Key.OemQuotes, "'");
        Add(Key.OemOpenBrackets, "["); Add(Key.OemCloseBrackets, "]"); Add(Key.OemPipe, "\\"); Add(Key.OemTilde, "`");

        return map;
    }

    private static List<(string, ushort)> BuildEntries()
    {
        // Win32 virtual-key codes (winuser.h). Letters/digits use their ASCII values directly.
        var list = new List<(string, ushort)>();

        for (char c = 'A'; c <= 'Z'; c++) list.Add((c.ToString(), (ushort)c));
        for (char c = '0'; c <= '9'; c++) list.Add((c.ToString(), (ushort)c));
        for (int i = 1; i <= 24; i++) list.Add(($"F{i}", (ushort)(0x70 + i - 1))); // VK_F1 = 0x70
        for (int i = 0; i <= 9; i++) list.Add(($"Numpad {i}", (ushort)(0x60 + i))); // VK_NUMPAD0 = 0x60

        list.Add(("Space", 0x20));
        list.Add(("Enter", 0x0D));
        list.Add(("Escape", 0x1B));
        list.Add(("Tab", 0x09));
        list.Add(("Backspace", 0x08));
        list.Add(("Delete", 0x2E));
        list.Add(("Insert", 0x2D));
        list.Add(("Home", 0x24));
        list.Add(("End", 0x23));
        list.Add(("Page Up", 0x21));
        list.Add(("Page Down", 0x22));
        list.Add(("Up Arrow", 0x26));
        list.Add(("Down Arrow", 0x28));
        list.Add(("Left Arrow", 0x25));
        list.Add(("Right Arrow", 0x27));
        list.Add(("Left Shift", 0xA0));
        list.Add(("Right Shift", 0xA1));
        list.Add(("Left Ctrl", 0xA2));
        list.Add(("Right Ctrl", 0xA3));
        list.Add(("Left Alt", 0xA4));
        list.Add(("Right Alt", 0xA5));
        list.Add(("Caps Lock", 0x14));
        list.Add(("Num Lock", 0x90));
        list.Add(("Scroll Lock", 0x91));
        list.Add(("Numpad *", 0x6A));
        list.Add(("Numpad +", 0x6B));
        list.Add(("Numpad -", 0x6D));
        list.Add(("Numpad .", 0x6E));
        list.Add(("Numpad /", 0x6F));
        list.Add(("-", 0xBD));
        list.Add(("=", 0xBB));
        list.Add((",", 0xBC));
        list.Add((".", 0xBE));
        list.Add(("/", 0xBF));
        list.Add((";", 0xBA));
        list.Add(("'", 0xDE));
        list.Add(("[", 0xDB));
        list.Add(("]", 0xDD));
        list.Add(("\\", 0xDC));
        list.Add(("`", 0xC0));

        return list;
    }
}
