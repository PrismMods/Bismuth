using System;
using UnityEngine;

namespace Bismuth
{
    internal partial class KeyViewer
    {
        internal static bool TryParseKey(string token, out KeyCode result)
        {
            switch (token)
            {
                case "Tab":       result = KeyCode.Tab; return true;
                case "Caps":      result = KeyCode.CapsLock; return true;
                case "Space":     result = KeyCode.Space; return true;
                case "Enter":     result = KeyCode.Return; return true;
                case "Backspace": result = KeyCode.Backspace; return true;
                case "Escape":    result = KeyCode.Escape; return true;
                case "LShift":    result = KeyCode.LeftShift; return true;
                case "RShift":    result = KeyCode.RightShift; return true;
                case "LCtrl":     result = KeyCode.LeftControl; return true;
                case "RCtrl":     result = KeyCode.RightControl; return true;
                case "LAlt":      result = KeyCode.LeftAlt; return true;
                case "RAlt":      result = KeyCode.RightAlt; return true;
                case "LCmd":      result = KeyCode.LeftCommand; return true;
                case "RCmd":      result = KeyCode.RightCommand; return true;
                case "Up":        result = KeyCode.UpArrow; return true;
                case "Down":      result = KeyCode.DownArrow; return true;
                case "Left":      result = KeyCode.LeftArrow; return true;
                case "Right":     result = KeyCode.RightArrow; return true;
                case "[":         result = KeyCode.LeftBracket; return true;
                case "]":         result = KeyCode.RightBracket; return true;
                case "\\":        result = KeyCode.Backslash; return true;
                case ";":         result = KeyCode.Semicolon; return true;
                case "'":         result = KeyCode.Quote; return true;
                case ",":         result = KeyCode.Comma; return true;
                case ".":         result = KeyCode.Period; return true;
                case "/":         result = KeyCode.Slash; return true;
                case "`":         result = KeyCode.BackQuote; return true;
                case "-":         result = KeyCode.Minus; return true;
                case "=":         result = KeyCode.Equals; return true;
                case "LMB":       result = KeyCode.Mouse0; return true;
                case "RMB":       result = KeyCode.Mouse1; return true;
                default:
                    if (token.Length == 1)
                    {
                        char c = char.ToUpperInvariant(token[0]);
                        if (c >= 'A' && c <= 'Z') { result = (KeyCode)((int)KeyCode.A + (c - 'A')); return true; }
                        if (c >= '0' && c <= '9') { result = (KeyCode)((int)KeyCode.Alpha0 + (c - '0')); return true; }
                    }
                    try { result = (KeyCode)Enum.Parse(typeof(KeyCode), token, true); return true; }
                    catch { result = KeyCode.None; return false; }
            }
        }

        private static string GetDisplayName(KeyCode key)
        {
            switch (key)
            {
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
                case KeyCode.Tab:          return "⇥";
                case KeyCode.Space:        return "⎵";
                case KeyCode.Return:       return "⏎";
                case KeyCode.Backspace:    return "Back";
                case KeyCode.Escape:       return "Esc";
                case KeyCode.LeftShift:    return "L⇧";
                case KeyCode.RightShift:   return "R⇧";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.UpArrow:      return "↑";
                case KeyCode.DownArrow:    return "↓";
                case KeyCode.LeftArrow:    return "←";
                case KeyCode.RightArrow:   return "→";
                case KeyCode.Mouse0:       return "LMB";
                case KeyCode.Mouse1:       return "RMB";
                default:
                    string s = key.ToString();
                    if (s.StartsWith("Alpha")) return s.Substring(5);
                    return s;
            }
        }
    }
}
