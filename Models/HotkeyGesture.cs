using System.Windows.Input;

namespace TomatoFlow.Models;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public sealed class HotkeyGesture
{
    private static readonly Dictionary<Key, string> KeyNames = new()
    {
        [Key.OemPlus] = "Plus",
        [Key.OemMinus] = "Minus",
        [Key.OemComma] = "Comma",
        [Key.OemPeriod] = "Period",
        [Key.OemQuestion] = "Slash",
        [Key.OemTilde] = "Tilde",
        [Key.OemOpenBrackets] = "BracketLeft",
        [Key.OemCloseBrackets] = "BracketRight",
        [Key.OemPipe] = "Backslash",
        [Key.OemQuotes] = "Quote",
        [Key.OemSemicolon] = "Semicolon",
        [Key.Space] = "Space",
        [Key.Return] = "Enter",
        [Key.Escape] = "Escape",
        [Key.Tab] = "Tab",
        [Key.Back] = "Backspace",
        [Key.Delete] = "Delete",
        [Key.Insert] = "Insert",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "PageUp",
        [Key.PageDown] = "PageDown",
        [Key.Left] = "Left",
        [Key.Right] = "Right",
        [Key.Up] = "Up",
        [Key.Down] = "Down"
    };

    private static readonly Dictionary<string, Key> ParsedNames = KeyNames
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public HotkeyGesture(HotkeyModifiers modifiers, int virtualKey, string display)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Display = display;
    }

    public HotkeyModifiers Modifiers { get; }
    public int VirtualKey { get; }
    public string Display { get; }

    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44, "Ctrl+Alt+D");

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    }

    public static HotkeyGesture? Create(ModifierKeys modifiers, Key key)
    {
        if (key == Key.System)
        {
            return null;
        }

        var hotkeyModifiers = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            hotkeyModifiers |= HotkeyModifiers.Control;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            hotkeyModifiers |= HotkeyModifiers.Alt;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            hotkeyModifiers |= HotkeyModifiers.Shift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            hotkeyModifiers |= HotkeyModifiers.Win;
        }

        if (key == Key.None)
        {
            return null;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return null;
        }

        var display = BuildDisplay(hotkeyModifiers, GetKeyName(key));
        return new HotkeyGesture(hotkeyModifiers, virtualKey, display);
    }

    public static HotkeyGesture? Create(ModifierKeys modifiers, Key key, bool allowBareFunctionKey)
    {
        var gesture = Create(modifiers, key);
        if (gesture is null)
        {
            return null;
        }

        if (gesture.Modifiers == HotkeyModifiers.None && !allowBareFunctionKey)
        {
            return null;
        }

        return gesture;
    }

    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? keyToken = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                case "super":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (keyToken is not null)
                    {
                        return false;
                    }

                    keyToken = part;
                    break;
            }
        }

        if (string.IsNullOrEmpty(keyToken))
        {
            return false;
        }

        if (!TryParseKey(keyToken, out var key))
        {
            return false;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey, BuildDisplay(modifiers, GetKeyName(key)));
        return true;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        if (ParsedNames.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return Enum.TryParse(character.ToString(), out key);
            }
        }

        if (token.StartsWith('F') && int.TryParse(token[1..], out var functionNumber) && functionNumber is >= 1 and <= 24)
        {
            return Enum.TryParse($"F{functionNumber}", out key);
        }

        try
        {
            var converter = new KeyConverter();
            if (converter.ConvertFromString(token) is Key parsed)
            {
                key = parsed;
                return true;
            }
        }
        catch
        {
        }

        key = Key.None;
        return false;
    }

    private static string GetKeyName(Key key)
    {
        if (KeyNames.TryGetValue(key, out var name))
        {
            return name;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"NumPad{(int)key - (int)Key.NumPad0}";
        }

        return key.ToString();
    }

    private static string BuildDisplay(HotkeyModifiers modifiers, string keyName)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
