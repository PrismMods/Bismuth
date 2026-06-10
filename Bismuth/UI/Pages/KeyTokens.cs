using UnityEngine;

namespace Bismuth.UI.Pages
{
    // Shared key-token helpers. Mirrors the friendly-name table in KeyViewer.Keys.cs so
    // tokens round-trip identically between the editor UI and the runtime parser.
    internal static class KeyTokens
    {
        public static string TokenFromKeyCode(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.Tab:          return "Tab";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Space:        return "Space";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Backspace";
                case KeyCode.Escape:       return "Escape";
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.UpArrow:      return "Up";
                case KeyCode.DownArrow:    return "Down";
                case KeyCode.LeftArrow:    return "Left";
                case KeyCode.RightArrow:   return "Right";
                case KeyCode.LeftBracket:  return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Backslash:    return "\\";
                case KeyCode.Semicolon:    return ";";
                case KeyCode.Quote:        return "'";
                case KeyCode.Comma:        return ",";
                case KeyCode.Period:       return ".";
                case KeyCode.Slash:        return "/";
                case KeyCode.BackQuote:    return "`";
                case KeyCode.Minus:        return "-";
                case KeyCode.Equals:       return "=";
            }
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
                return ((int)(kc - KeyCode.Alpha0)).ToString();
            return kc.ToString();
        }

        public static string PrettyTokenLabel(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            // Special-token passthrough (KPS / Total are not real keys).
            if (token == "KPS" || token == "Total") return token;
            if (!KeyViewer.TryParseKey(token, out KeyCode kc)) return token;
            switch (kc)
            {
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Back";
                case KeyCode.Escape:       return "Esc";
                case KeyCode.UpArrow:      return "↑";
                case KeyCode.DownArrow:    return "↓";
                case KeyCode.LeftArrow:    return "←";
                case KeyCode.RightArrow:   return "→";
                default:                   return token;
            }
        }
    }
}
