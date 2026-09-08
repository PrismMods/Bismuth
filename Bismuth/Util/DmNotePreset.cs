using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Bismuth.UI.Pages;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bismuth
{
    /* DM Note (github.com/DmNote-App/DmNote) preset.json ↔ KeyViewerPreset. Lossy both
       ways: DM Note places keys freely (dx/dy per key), Bismuth in rows — import clusters
       keys by dy, export replays BuildPresetPanel's row math. Files live in <mod>/DmNote/,
       the same drop-in-folder flow as Profiles. Key names are DM Note's globalKey strings
       ("A", "LEFT SHIFT", "NUMPAD 5"); a few are bare Windows VK codes ("21" = Hangul). */
    internal static class DmNotePreset
    {
        private const float Pad = 30f;       // margin around the exported layout
        private const string Tab = "4key";   // DM Note tab id; the key count isn't enforced
        private static string Dir => Path.Combine(MainClass.ModPath, "DmNote");

        internal static string DirPath()
        {
            try { Directory.CreateDirectory(Dir); } catch { }
            return Dir;
        }

        internal static List<string> ListFiles()
        {
            var result = new List<string>();
            try
            {
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.json"))
                        result.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception e) { BismuthLog.Log("DmNote: list failed: " + e.Message); }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        internal static bool Export(KeyViewerPreset preset, out string path, out string error)
        {
            path = null;
            error = null;
            string name = Profiles.SanitizeName(preset.Name);
            if (name.Length == 0) name = "preset";
            try
            {
                path = Path.Combine(DirPath(), name + ".json");
                File.WriteAllText(path, ToJson(preset).ToString(Newtonsoft.Json.Formatting.Indented));
                BismuthLog.Log("DmNote: exported '" + preset.Name + "' to " + path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                BismuthLog.Log("DmNote: export failed: " + e);
                return false;
            }
        }

        internal static bool Import(string name, out KeyViewerPreset preset, out string error)
        {
            preset = null;
            error = null;
            try
            {
                string path = Path.Combine(Dir, name + ".json");
                if (!File.Exists(path)) { error = "File not found."; return false; }
                preset = FromJson(JObject.Parse(File.ReadAllText(path)), name);
                BismuthLog.Log("DmNote: imported '" + name + "' (" + preset.Rows.Count + " rows)");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                BismuthLog.Log("DmNote: import failed: " + e);
                return false;
            }
        }

        // ── Key names ──────────────────────────────────────────────────────

        // DM Note's globalKey table (src/renderer/utils/input/KeyMaps.ts). Letters, digits,
        // F-keys and numpad digits are derived.
        private static readonly Dictionary<KeyCode, string> _toDm = new Dictionary<KeyCode, string>
        {
            { KeyCode.LeftShift, "LEFT SHIFT" }, { KeyCode.RightShift, "RIGHT SHIFT" },
            { KeyCode.LeftControl, "LEFT CTRL" }, { KeyCode.RightControl, "25" },
            { KeyCode.LeftAlt, "LEFT ALT" }, { KeyCode.RightAlt, "21" },
            { KeyCode.LeftCommand, "91" }, { KeyCode.RightCommand, "92" },
            { KeyCode.Space, "SPACE" }, { KeyCode.Return, "RETURN" }, { KeyCode.Tab, "TAB" },
            { KeyCode.Backspace, "BACKSPACE" }, { KeyCode.CapsLock, "CAPS LOCK" }, { KeyCode.Escape, "ESCAPE" },
            { KeyCode.UpArrow, "UP ARROW" }, { KeyCode.DownArrow, "DOWN ARROW" },
            { KeyCode.LeftArrow, "LEFT ARROW" }, { KeyCode.RightArrow, "RIGHT ARROW" },
            { KeyCode.Minus, "MINUS" }, { KeyCode.Equals, "EQUALS" },
            { KeyCode.LeftBracket, "SQUARE BRACKET OPEN" }, { KeyCode.RightBracket, "SQUARE BRACKET CLOSE" },
            { KeyCode.Semicolon, "SEMICOLON" }, { KeyCode.Quote, "QUOTE" }, { KeyCode.BackQuote, "SECTION" },
            { KeyCode.Backslash, "BACKSLASH" }, { KeyCode.Comma, "COMMA" }, { KeyCode.Period, "DOT" },
            { KeyCode.Slash, "FORWARD SLASH" },
            { KeyCode.KeypadMultiply, "NUMPAD MULTIPLY" }, { KeyCode.KeypadPlus, "NUMPAD PLUS" },
            { KeyCode.KeypadMinus, "NUMPAD MINUS" }, { KeyCode.KeypadPeriod, "NUMPAD DELETE" },
            { KeyCode.KeypadDivide, "NUMPAD DIVIDE" }, { KeyCode.KeypadEnter, "NUMPAD RETURN" },
            { KeyCode.Print, "PRINT SCREEN" }, { KeyCode.ScrollLock, "SCROLL LOCK" }, { KeyCode.Pause, "19" },
            { KeyCode.Insert, "INS" }, { KeyCode.Home, "HOME" }, { KeyCode.PageUp, "PAGE UP" },
            { KeyCode.Delete, "DELETE" }, { KeyCode.End, "END" }, { KeyCode.PageDown, "PAGE DOWN" },
            { KeyCode.Menu, "CONTEXT MENU" },
            { KeyCode.Mouse0, "MOUSE1" }, { KeyCode.Mouse1, "MOUSE2" }, { KeyCode.Mouse2, "MOUSE3" },
            { KeyCode.Mouse3, "MOUSE4" }, { KeyCode.Mouse4, "MOUSE5" },
        };

        // Names other exporters (KRP, older DM Note builds) use for the same keys, normalized
        // like FromDmName does (no spaces, upper).
        private static readonly Dictionary<string, KeyCode> _aliases = new Dictionary<string, KeyCode>
        {
            { "ENTER", KeyCode.Return }, { "ESC", KeyCode.Escape }, { "PERIOD", KeyCode.Period },
            { "SLASH", KeyCode.Slash }, { "INSERT", KeyCode.Insert }, { "CAPSLOCK", KeyCode.CapsLock },
            { "EQUAL", KeyCode.Equals }, { "PLUS", KeyCode.Plus },
            { "BACKQUOTE", KeyCode.BackQuote }, { "BACKTICK", KeyCode.BackQuote },
            { "LSHIFT", KeyCode.LeftShift }, { "RSHIFT", KeyCode.RightShift },
            { "LCTRL", KeyCode.LeftControl }, { "LCONTROL", KeyCode.LeftControl }, { "LEFTCONTROL", KeyCode.LeftControl },
            { "CTRL", KeyCode.LeftControl }, { "CONTROL", KeyCode.LeftControl },
            { "RCTRL", KeyCode.RightControl }, { "RCONTROL", KeyCode.RightControl }, { "RIGHTCONTROL", KeyCode.RightControl },
            { "RIGHTCTRL", KeyCode.RightControl }, { "HANJA", KeyCode.RightControl },
            { "LALT", KeyCode.LeftAlt }, { "RALT", KeyCode.RightAlt }, { "RIGHTALT", KeyCode.RightAlt },
            { "ALTGR", KeyCode.RightAlt }, { "HANGUL", KeyCode.RightAlt },
            { "LEFTBRACKET", KeyCode.LeftBracket }, { "LBRACKET", KeyCode.LeftBracket }, { "OPENBRACKET", KeyCode.LeftBracket },
            { "RIGHTBRACKET", KeyCode.RightBracket }, { "RBRACKET", KeyCode.RightBracket }, { "CLOSEBRACKET", KeyCode.RightBracket },
            { "UP", KeyCode.UpArrow }, { "DOWN", KeyCode.DownArrow }, { "LEFT", KeyCode.LeftArrow }, { "RIGHT", KeyCode.RightArrow },
            { "NUMPADENTER", KeyCode.KeypadEnter }, { "NUMPADADD", KeyCode.KeypadPlus }, { "NUMPADSUBTRACT", KeyCode.KeypadMinus },
            { "NUMPADDECIMAL", KeyCode.KeypadPeriod }, { "NUMPADPERIOD", KeyCode.KeypadPeriod }, { "NUMPADDOT", KeyCode.KeypadPeriod },
            { "NUMPADSTAR", KeyCode.KeypadMultiply }, { "NUMPADEQUALS", KeyCode.KeypadEquals },
            { "CONTEXTMENU", KeyCode.Menu }, { "APPS", KeyCode.Menu },
        };

        // Windows virtual-key codes DM Note stores as bare numbers (keys its label table lacks).
        private static readonly Dictionary<int, KeyCode> _vk = new Dictionary<int, KeyCode>
        {
            { 19, KeyCode.Pause }, { 21, KeyCode.RightAlt }, { 25, KeyCode.RightControl },
            { 91, KeyCode.LeftCommand }, { 92, KeyCode.RightCommand }, { 93, KeyCode.Menu },
        };

        private static Dictionary<string, KeyCode> _fromDm;

        internal static string ToDmName(KeyCode kc)
        {
            if (_toDm.TryGetValue(kc, out string s)) return s;
            if (kc >= KeyCode.A && kc <= KeyCode.Z) return ((char)('A' + (kc - KeyCode.A))).ToString();
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9) return ((char)('0' + (kc - KeyCode.Alpha0))).ToString();
            if (kc >= KeyCode.Keypad0 && kc <= KeyCode.Keypad9) return "NUMPAD " + (kc - KeyCode.Keypad0);
            return kc.ToString().ToUpperInvariant();   // F1…F15 etc. — DM Note's own fallback naming
        }

        internal static KeyCode FromDmName(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            string n = name.Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
            if (n.Length == 0) return KeyCode.None;
            if (n.Length > 1 && int.TryParse(n, out int vk))
                return _vk.TryGetValue(vk, out KeyCode mapped) ? mapped
                     : Enum.IsDefined(typeof(KeyCode), vk) ? (KeyCode)vk : KeyCode.None;
            // KeyboardEvent.code style ("KeyA", "Digit1") from older exports.
            if (n.Length == 4 && n.StartsWith("KEY")) n = n.Substring(3);
            if (n.Length == 6 && n.StartsWith("DIGIT")) n = n.Substring(5);
            if (_fromDm == null)
            {
                _fromDm = new Dictionary<string, KeyCode>();
                foreach (var kv in _toDm) _fromDm[kv.Value.Replace(" ", "")] = kv.Key;
                for (int i = 0; i < 10; i++) _fromDm["NUMPAD" + i] = KeyCode.Keypad0 + i;
            }
            if (_fromDm.TryGetValue(n, out KeyCode kc) || _aliases.TryGetValue(n, out kc)) return kc;
            if (n.Length == 1)
            {
                char c = n[0];
                if (c >= 'A' && c <= 'Z') return KeyCode.A + (c - 'A');
                if (c >= '0' && c <= '9') return KeyCode.Alpha0 + (c - '0');
            }
            // Unity's own names ("LeftShift"). Digit-led strings would parse as raw enum values.
            if (!char.IsDigit(n[0]) && Enum.TryParse(n, true, out kc)) return kc;
            return KeyCode.None;
        }

        // ── Colors ─────────────────────────────────────────────────────────

        private static readonly KvColor White = new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };

        private static string Rgba(KvColor c)
        {
            c = c ?? White;
            return string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3:0.##})",
                Mathf.RoundToInt(Mathf.Clamp01(c.R) * 255f), Mathf.RoundToInt(Mathf.Clamp01(c.G) * 255f),
                Mathf.RoundToInt(Mathf.Clamp01(c.B) * 255f), Mathf.Clamp01(c.A));
        }

        private static string Hex(KvColor c) => "#" + ColorUtility.ToHtmlStringRGB((c ?? White).ToColor());

        // CSS color subset DM Note writes: #RGB[A] / #RRGGBB[AA], rgb()/rgba(), "transparent".
        // null = unparseable (callers fall back to defaults).
        internal static KvColor ParseColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return new KvColor();
            try
            {
                if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    int lp = s.IndexOf('('), rp = s.LastIndexOf(')');
                    if (lp < 0 || rp <= lp) return null;
                    var parts = s.Substring(lp + 1, rp - lp - 1).Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) return null;
                    return new KvColor
                    {
                        R = Comp(parts[0], 255f), G = Comp(parts[1], 255f), B = Comp(parts[2], 255f),
                        A = parts.Length > 3 ? Comp(parts[3], 1f) : 1f,
                    };
                }
                string h = s.TrimStart('#');
                if (h.Length == 3 || h.Length == 4)
                    h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] }) + (h.Length == 4 ? new string(h[3], 2) : "");
                if (h.Length != 6 && h.Length != 8) return null;
                return new KvColor
                {
                    R = Convert.ToInt32(h.Substring(0, 2), 16) / 255f,
                    G = Convert.ToInt32(h.Substring(2, 2), 16) / 255f,
                    B = Convert.ToInt32(h.Substring(4, 2), 16) / 255f,
                    A = h.Length == 8 ? Convert.ToInt32(h.Substring(6, 2), 16) / 255f : 1f,
                };
            }
            catch { return null; }
        }

        // "255" / "100%" / "0.9" → 0..1; `scale` is the unit-less full-scale value.
        private static float Comp(string v, float scale)
        {
            v = v.Trim();
            bool pct = v.EndsWith("%");
            if (pct) v = v.Substring(0, v.Length - 1);
            if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return 1f;
            return Mathf.Clamp01(pct ? f / 100f : f / scale);
        }

        // noteColor is a hex string or {type:"gradient", top, bottom}; noteOpacity is 0..100.
        private static KvColor NoteColor(JObject p)
        {
            var tok = p["noteColor"];
            string hex = tok is JObject g ? S(g, "bottom", S(g, "top", "#FFFFFF"))
                       : tok == null || tok.Type == JTokenType.Null ? "#FFFFFF" : tok.ToString();
            var c = ParseColor(hex) ?? new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };
            c.A = Mathf.Clamp01(F(p, "noteOpacityBottom", F(p, "noteOpacity", 90f)) / 100f);
            return c;
        }

        // ── Export ─────────────────────────────────────────────────────────

        private class Placed
        {
            public KeyViewerCell Cell;
            public KeyCode Key;        // None for KPS / Total
            public int Index;          // position within its row's cell list
            public float Left, W, Cx, RainX;
        }

        internal static JObject ToJson(KeyViewerPreset p)
        {
            var rows = new List<KeyViewerRow>();
            foreach (var r in p.Rows ?? new List<KeyViewerRow>())
            {
                if (r == null) continue;
                r.EnsureDefaults();
                if (r.Cells.Count > 0) rows.Add(r);
            }
            if (rows.Count == 0) throw new Exception("preset has no rows");

            float keyW = p.KeyWidth, gap = p.Gap;
            int topN = rows[0].Cells.Count;
            float panelW = topN * keyW + Mathf.Max(0, topN - 1) * gap;

            var keys = new JArray();
            var keyPos = new JArray();
            var statPos = new JArray();
            var topKeyX = new List<float>();
            float yTop = 0f;
            int z = 0;

            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri];
                var cells = row.Cells;
                float sumMul = 0f;
                foreach (var c in cells) sumMul += c.WidthMul;
                if (sumMul <= 0f) sumMul = 1f;
                float rowTop = yTop;
                yTop += row.Height;
                float cellH = row.Height - gap;

                // Same slot math as BuildPresetPanel: the top row's visible widths sum to
                // topN*keyW; lower rows split the panel width and lose the gap inside each slot.
                var placed = new List<Placed>();
                float cursor = 0f;
                for (int ci = 0; ci < cells.Count; ci++)
                {
                    var cell = cells[ci];
                    float left, w;
                    if (ri == 0) { w = topN * keyW * cell.WidthMul / sumMul; left = cursor; cursor += w + gap; }
                    else { float slot = panelW * cell.WidthMul / sumMul; w = slot - gap; left = cursor + gap * 0.5f; cursor += slot; }
                    bool stat = cell.Token == "KPS" || cell.Token == "Total";
                    var x = new Placed { Cell = cell, Index = ci, Left = left, W = w, Cx = left + w * 0.5f };
                    x.RainX = x.Cx;
                    x.Key = !stat && KeyViewer.TryParseKey(cell.Token, out KeyCode kc) ? kc : KeyCode.None;
                    placed.Add(x);
                }

                // Lower rows with fewer keys than the top row spawn rain in the top row's column
                // slots (left half left-aligned, right half right-aligned to the midpoint) —
                // noteOffsetX carries that shift.
                var keyed = placed.FindAll(x => x.Key != KeyCode.None);
                if (ri == 0) foreach (var x in keyed) topKeyX.Add(x.Cx);
                else if (keyed.Count < topKeyX.Count)
                {
                    int halfN = cells.Count / 2, topM = topKeyX.Count / 2;
                    var left = keyed.FindAll(x => x.Index < halfN);
                    var right = keyed.FindAll(x => x.Index >= halfN);
                    for (int i = 0; i < left.Count; i++)
                    {
                        int slot = topM - left.Count + i;
                        if (slot >= 0 && slot < topKeyX.Count) left[i].RainX = topKeyX[slot];
                    }
                    for (int i = 0; i < right.Count; i++)
                    {
                        int slot = topM + i;
                        if (slot >= 0 && slot < topKeyX.Count) right[i].RainX = topKeyX[slot];
                    }
                }
                float rainW = row.RainWidth > 0f ? row.RainWidth : Mathf.Max(4f, keyW - ri * p.RainWidthStep);

                foreach (var x in placed)
                {
                    var jp = Position(p, row, x, Pad + x.Left, Pad + rowTop + gap * 0.5f, cellH, rainW, z++);
                    if (x.Cell.Token == "KPS" || x.Cell.Token == "Total")
                    {
                        jp["statType"] = x.Cell.Token == "KPS" ? "kps" : "total";
                        statPos.Add(jp);
                        continue;
                    }
                    if (x.Key == KeyCode.None)
                    {
                        BismuthLog.Log("DmNote: skipping unknown key token '" + x.Cell.Token + "'");
                        continue;
                    }
                    // Ghost slots index the top row's keys; `keys` holds exactly those so far.
                    if (ri == 0 && p.GhostKeysEnabled && p.GhostKeys != null && keys.Count < p.GhostKeys.Count
                        && KeyViewer.TryParseKey(p.GhostKeys[keys.Count] ?? "", out KeyCode g) && g != KeyCode.None)
                        jp["ghostKey"] = ToDmName(g);
                    keys.Add(ToDmName(x.Key));
                    keyPos.Add(jp);
                }
            }

            return new JObject
            {
                ["selectedKeyType"] = Tab,
                ["keys"] = new JObject { [Tab] = keys },
                ["keyPositions"] = new JObject { [Tab] = keyPos },
                ["statPositions"] = new JObject { [Tab] = statPos },
            };
        }

        private static JObject Position(KeyViewerPreset p, KeyViewerRow row, Placed x, float dx, float dy, float h, float rainW, int z)
        {
            bool stat = x.Key == KeyCode.None;
            var rain = row.RainColor ?? White;
            var jp = new JObject
            {
                ["dx"] = R(dx), ["dy"] = R(dy), ["width"] = R(x.W), ["height"] = R(h),
                ["count"] = 0, ["hidden"] = false, ["zIndex"] = z,
                ["fontSize"] = x.Cell.LabelSize > 0 ? x.Cell.LabelSize : p.LabelSize,
                ["fontColor"] = Rgba(p.TxtIdle), ["activeFontColor"] = Rgba(p.TxtHeld),
                ["backgroundColor"] = Rgba(p.BgIdle), ["activeBackgroundColor"] = Rgba(p.BgHeld),
                ["idleTransparent"] = !p.ShowBackground, ["activeTransparent"] = !p.ShowBackground,
                ["borderColor"] = Rgba(p.BorderIdle), ["activeBorderColor"] = Rgba(p.BorderHeld),
                ["borderWidth"] = p.ShowBorder ? R(p.BorderWidth) : 0f, ["borderRadius"] = p.Radius,
                ["noteColor"] = Hex(rain), ["noteOpacity"] = Mathf.RoundToInt(Mathf.Clamp01(rain.A) * 100f),
                ["noteEffectEnabled"] = !stat && p.ShowRain && row.ShowRain,
                ["noteWidth"] = R(rainW), ["noteAlignment"] = "center", ["noteAutoYCorrection"] = true,
                ["counter"] = new JObject
                {
                    ["enabled"] = stat || p.ShowCount, ["placement"] = "inside", ["align"] = "bottom",
                    ["alignMode"] = "center", ["gap"] = 4, ["fontSize"] = p.CountSize,
                    ["fill"] = new JObject { ["idle"] = Rgba(p.CountIdle), ["active"] = Rgba(p.CountHeld) },
                },
            };
            if (!stat && Mathf.Abs(x.RainX - x.Cx) > 0.5f) jp["noteOffsetX"] = R(x.RainX - x.Cx);
            if (!string.IsNullOrEmpty(x.Cell.Label)) jp["displayText"] = x.Cell.Label;
            return jp;
        }

        private static float R(float v) => Mathf.Round(v * 100f) / 100f;

        // ── Import ─────────────────────────────────────────────────────────

        private class Item
        {
            public readonly string Token;
            public readonly JObject P;
            public readonly float Left, Top, W, H, Cx, Cy;
            public bool IsStat => Token == "KPS" || Token == "Total";

            public Item(string token, JObject p, float defW, float defH)
            {
                Token = token;
                P = p;
                Left = F(p, "dx", 0f);
                Top = F(p, "dy", 0f);
                W = Mathf.Max(1f, F(p, "width", defW));
                H = Mathf.Max(1f, F(p, "height", defH));
                Cx = Left + W * 0.5f;
                Cy = Top + H * 0.5f;
            }
        }

        internal static KeyViewerPreset FromJson(JObject o, string name)
        {
            var keysTable = o["keys"] as JObject;
            var posTable = (o["keyPositions"] as JObject) ?? (o["positions"] as JObject);
            string tab = PickTab(o, keysTable, posTable);
            var keyArr = keysTable?[tab] as JArray;
            var posArr = posTable?[tab] as JArray;
            if (keyArr == null || posArr == null) throw new Exception("no key layout found in this preset");

            var items = new List<Item>();
            int n = Math.Min(keyArr.Count, posArr.Count);
            for (int i = 0; i < n; i++)
            {
                if (!(posArr[i] is JObject jp) || B(jp, "hidden", false)) continue;
                string dm = SlotName(keyArr[i]);
                if (dm.Length == 0) continue;
                KeyCode kc = FromDmName(dm);
                if (kc == KeyCode.None) { BismuthLog.Log("DmNote: unknown key '" + dm + "' skipped"); continue; }
                items.Add(new Item(KeyTokens.TokenFromKeyCode(kc), jp, 60f, 60f));
            }
            if (o["statPositions"] is JObject statTable && statTable[tab] is JArray statArr)
                foreach (var t in statArr)
                {
                    if (!(t is JObject sp)) continue;
                    var jp = (sp["position"] as JObject) ?? sp;
                    if (B(sp, "hidden", false) || B(jp, "hidden", false)) continue;
                    string type = S(sp, "statType", S(jp, "statType", ""));
                    string tok = type.Equals("kps", StringComparison.OrdinalIgnoreCase) ? "KPS"
                               : type.Equals("total", StringComparison.OrdinalIgnoreCase) ? "Total" : null;
                    if (tok != null) items.Add(new Item(tok, jp, 100f, 30f));   // kpsAvg/kpsMax have no cell
                }
            if (items.Count == 0) throw new Exception("preset has no visible keys");

            // Rows: cluster by vertical center within half a key's height; left to right inside.
            items.Sort((a, b) => a.Cy != b.Cy ? a.Cy.CompareTo(b.Cy) : a.Cx.CompareTo(b.Cx));
            var rows = new List<List<Item>>();
            foreach (var it in items)
            {
                var last = rows.Count > 0 ? rows[rows.Count - 1] : null;
                if (last != null && Mathf.Abs(it.Cy - last[0].Cy) < it.H * 0.5f) last.Add(it);
                else rows.Add(new List<Item> { it });
            }
            foreach (var r in rows) r.Sort((a, b) => a.Cx.CompareTo(b.Cx));

            var widths = new List<float>();
            var gaps = new List<float>();
            foreach (var r in rows)
                for (int i = 0; i < r.Count; i++)
                {
                    if (!r[i].IsStat) widths.Add(r[i].W);
                    if (i > 0) gaps.Add(r[i].Left - (r[i - 1].Left + r[i - 1].W));
                }
            float keyW = Mathf.Clamp(Median(widths, 60f), 20f, 200f);
            float gap = Mathf.Clamp(Median(gaps, 4f), 0f, 30f);

            // Preset-level style comes from the first key; DM Note defaults where absent.
            var style = (items.Find(x => !x.IsStat) ?? items[0]).P;
            var counter = style["counter"] as JObject;
            var fill = counter?["fill"] as JObject;
            string font = S(style, "fontColor", "rgba(121, 121, 121, 0.9)");
            string activeFont = S(style, "activeFontColor", "#FFFFFF");
            var p = new KeyViewerPreset
            {
                Name = name, KeyWidth = keyW, Gap = gap, Rows = new List<KeyViewerRow>(),
                Radius = Mathf.RoundToInt(F(style, "borderRadius", 10f)),
                BorderWidth = F(style, "borderWidth", 3f),
                ShowBackground = !B(style, "idleTransparent", false),
                LabelSize = Mathf.RoundToInt(F(style, "fontSize", 18f)),
                ShowCount = counter == null || B(counter, "enabled", true),
                CountSize = counter != null ? Mathf.RoundToInt(F(counter, "fontSize", 16f)) : 16,
                BgIdle = ParseColor(S(style, "backgroundColor", "rgba(46, 46, 47, 0.9)")),
                BgHeld = ParseColor(S(style, "activeBackgroundColor", "rgba(121, 121, 121, 0.9)")),
                BorderIdle = ParseColor(S(style, "borderColor", "rgba(113, 113, 113, 0.9)")),
                BorderHeld = ParseColor(S(style, "activeBorderColor", "rgba(255, 255, 255, 0.9)")),
                TxtIdle = ParseColor(font),
                TxtHeld = ParseColor(activeFont),
                CountIdle = ParseColor(S(fill, "idle", font)),
                CountHeld = ParseColor(S(fill, "active", activeFont)),
            };
            p.ShowBorder = p.BorderWidth > 0.01f;

            bool anyRain = false;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var r = rows[ri];
                var heights = new List<float>();
                foreach (var it in r) heights.Add(it.H);
                var row = new KeyViewerRow { Height = Mathf.Clamp(Median(heights, 60f) + gap, 10f, 300f) };
                var lead = r.Find(x => !x.IsStat);
                if (lead != null)
                {
                    row.RainColor = NoteColor(lead.P);
                    row.RainColorCustom = true;
                    row.ShowRain = B(lead.P, "noteEffectEnabled", true);
                    anyRain |= row.ShowRain;
                }
                foreach (var it in r)
                {
                    int fs = Mathf.RoundToInt(F(it.P, "fontSize", p.LabelSize));
                    string label = it.IsStat ? null : S(it.P, "displayText", null);
                    row.Cells.Add(new KeyViewerCell
                    {
                        Token = it.Token,
                        Label = string.IsNullOrEmpty(label) ? null : label,
                        // Top row: visible width = keyW * mul. Lower rows: slot = (keyW + gap) * mul, minus the gap.
                        WidthMul = Mathf.Round((ri == 0 ? it.W / keyW : (it.W + gap) / (keyW + gap)) * 100f) / 100f,
                        LabelSize = fs != p.LabelSize ? fs : 0,
                    });
                }
                p.Rows.Add(row);
            }
            p.ShowRain = anyRain;

            // Ghost keys ride the top row's key slots (a KRP/Quartz extension of the format).
            var ghosts = new List<string>();
            bool anyGhost = false;
            foreach (var it in rows[0])
            {
                if (it.IsStat) continue;
                KeyCode g = FromDmName(S(it.P, "ghostKey", ""));
                ghosts.Add(g == KeyCode.None ? "None" : KeyTokens.TokenFromKeyCode(g));
                anyGhost |= g != KeyCode.None;
            }
            if (anyGhost) { p.GhostKeysEnabled = true; p.GhostKeys = ghosts; }

            p.EnsureDefaults();
            return p;
        }

        private static string PickTab(JObject o, JObject keys, JObject pos)
        {
            string sel = S(o, "selectedKeyType", null);
            if (!string.IsNullOrEmpty(sel) && keys?[sel] != null && pos?[sel] != null) return sel;
            if (keys != null)
                foreach (var prop in keys.Properties())
                    if (pos?[prop.Name] != null) return prop.Name;
            return sel ?? Tab;
        }

        // A slot is a key name, or {keys:[…], match} for multi-key slots — take the first key.
        private static string SlotName(JToken t)
        {
            if (t is JObject o) return (o["keys"] as JArray)?.Count > 0 ? (o["keys"][0]?.ToString() ?? "") : "";
            return t == null || t.Type == JTokenType.Null ? "" : t.ToString();
        }

        private static float Median(List<float> v, float def)
        {
            if (v.Count == 0) return def;
            v.Sort();
            return v[v.Count / 2];
        }

        private static string S(JObject p, string key, string def)
        {
            var t = p?[key];
            return t == null || t.Type == JTokenType.Null ? def : t.ToString();
        }

        private static float F(JObject p, string key, float def)
        {
            var t = p?[key];
            if (t == null || t.Type == JTokenType.Null) return def;
            return float.TryParse(t.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        private static bool B(JObject p, string key, bool def)
        {
            var t = p?[key];
            if (t == null || t.Type == JTokenType.Null) return def;
            if (t.Type == JTokenType.Boolean) return (bool)t;
            return bool.TryParse(t.ToString(), out bool v) ? v : def;
        }

        // Debug-mode round trip of the active hand preset; logs when row/token shape drifts.
        internal static void SelfCheck()
        {
            var src = MainClass.Settings?.Hand;
            if (src == null) return;
            try
            {
                string a = Shape(src), b = Shape(FromJson(ToJson(src), src.Name));
                BismuthLog.Debug(a == b ? "[dbg] DmNote round-trip OK: " + a
                                        : "[dbg] DmNote round-trip MISMATCH:\n  " + a + "\n  " + b);
            }
            catch (Exception e) { BismuthLog.Log("[dbg] DmNote self-check failed: " + e.Message); }
        }

        private static string Shape(KeyViewerPreset p)
        {
            var rows = new List<string>();
            foreach (var r in p.Rows)
            {
                var t = new List<string>();
                foreach (var c in r.Cells) t.Add(c.Token);
                rows.Add(string.Join(" ", t));
            }
            return string.Join(" | ", rows);
        }
    }
}
