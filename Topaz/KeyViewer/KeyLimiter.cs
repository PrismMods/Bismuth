using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Topaz.UI;
using HarmonyLib;
using UnityEngine;

namespace Topaz
{
    internal static class KeyLimiter
    {
        private static readonly HashSet<KeyCode> _allowed = new HashSet<KeyCode>();
        private static bool _active;

        // Block all game inputs while the Topaz menu is open.
        private static bool _blockWhileOpen;

        // Chatter blocker state
        private static bool  _chatterActive;
        private static float _chatterThresholdSec;

        // Ghost-key suppression. Active hand preset's ghost keys are never counted as input
        // by the game, even when the limiter and chatter blocker are both disabled.
        private static readonly HashSet<KeyCode> _ghosts = new HashSet<KeyCode>();
        // Last accepted press time per key (realtimeSinceStartup). Updated only when a press is NOT chatter.
        private static readonly Dictionary<KeyCode, float> _lastPressTime = new Dictionary<KeyCode, float>();
        // Per-frame idempotency: which keys we've already counted this frame (and their accept/chatter decision).
        // Prevents the second GetMain call in the same frame from misclassifying an already-accepted press as chatter.
        private static int _chatterFrame = -1;
        private static readonly Dictionary<KeyCode, bool> _chatterDecisionThisFrame = new Dictionary<KeyCode, bool>();

        // Reflection cache (initialised once on first key press)
        private static bool       _reflReady;
        private static MethodInfo _getStateKeys;      // RDInput.GetStateKeys — see InvokeGetStateKeys for both shapes
        private static FieldInfo  _anyKcValue;        // AnyKeyCode.value
        private static System.Type _asyncKcType;      // AsyncKeyCode
        private static FieldInfo  _asyncKcLabel;      // AsyncKeyCode.label (SkyHook.KeyLabel)
        private static FieldInfo  _asyncKcKey;        // AsyncKeyCode.key (ushort raw OS scancode)
        private static MethodInfo _unityToAsync;      // SkyHook.SkyHookKeyMapper.UnityKeyToSkyHookKey(KeyCode) → KeyLabel
        private static MethodInfo _asyncToUnity;      // SkyHook.SkyHookKeyMapper.SkyHookKeyToUnityKey(KeyLabel) → KeyCode
        private static MethodInfo _labelToNative;     // SkyHook.SkyHookKeyMapper.KeyLabelToNativeKeyCode(KeyLabel) → ushort
        private static System.Type _keyLabelType;     // SkyHook.KeyLabel
        private static object     _stateDown;         // ButtonState.WentDown (enum value 0)
        private static object     _stateWentUp;       // ButtonState.WentUp (1)
        private static object     _stateIsDown;       // ButtonState.IsDown (2)
        /* Reused single-element args buffer for the per-frame GetStateKeys.Invoke (called
           2-3× every frame while the key viewer runs). Reflection Invoke otherwise
           allocates a fresh object[] each call — steady GC pressure during play. Safe to
           share: all reads are main-thread and sequential, and the _inCount guard keeps the
           GetMain postfix from re-entering GetStateKeys mid-Invoke. */
        private static readonly object[] _stateArgs = new object[1];
        private static readonly object[] _stateArgs2 = new object[2];
        // The List<AnyKeyCode> the fill-in GetStateKeys shape writes into; null on the older
        // returning shape. Shared under the same main-thread, sequential guarantee as above.
        private static IList _stateKeysBuf;

        // Pre-computed set of allowed SkyHook KeyLabel values (ushort) for async keyboard path.
        // Built from _allowed via UnityKeyToSkyHookKey so we compare labels directly,
        // avoiding the ambiguity in SkyHookKeyToUnityKey (multiple KeyCodes share one label slot).
        private static readonly HashSet<ushort> _allowedLabels = new HashSet<ushort>();

        // Raw-key fallback for entries SkyHook labels Unknown. The byte's meaning is per
        // platform and the spaces collide (0x39 = HID CapsLock vs VK '9'): macOS = USB HID
        // usage IDs, Windows/Proton = Win32 VK codes, anything else = null (fail open).
        private static Dictionary<ushort, KeyCode> _rawToKeyCode;

        private static readonly Dictionary<ushort, KeyCode> _hidToKeyCode = new Dictionary<ushort, KeyCode>
        {
            { 0x34, KeyCode.Quote },
            { 0x35, KeyCode.BackQuote },
            { 0x39, KeyCode.CapsLock },
            { 0xE0, KeyCode.LeftControl },
            { 0xE1, KeyCode.LeftShift },
            { 0xE2, KeyCode.LeftAlt },
            { 0xE3, KeyCode.LeftCommand },
            { 0xE4, KeyCode.RightControl },
            { 0xE5, KeyCode.RightShift },
            { 0xE6, KeyCode.RightAlt },
            { 0xE7, KeyCode.RightCommand },
            { 0x65, KeyCode.Menu },   // HID Keyboard Application — SkyHook has no label for it
        };

        // SkyHook.KeyLabel: 119 = IgnoredInternal, 120 = Unknown. Neither names a key, and
        // UnityKeyToSkyHookKey returns Unknown for anything it can't map.
        private const ushort LabelIgnored = 119;
        private const ushort LabelUnknown = 120;

        /* SkyHookKeyMapper has no rows for ' or ` (the labels exist). Without these, an
           allowed ` put Unknown into _allowedLabels — matching every unnameable press —
           and the keys' own presses resolved to nothing. */
        private static readonly Dictionary<KeyCode, ushort> _extraKeyToLabel = new Dictionary<KeyCode, ushort>
        {
            { KeyCode.Quote,     64 },  // KeyLabel.Apostrophe
            { KeyCode.BackQuote, 25 },  // KeyLabel.Grave
            { KeyCode.Equals,    37 },  // KeyLabel.Equals — see _extraLabelToKey below
        };
        private static readonly Dictionary<ushort, KeyCode> _extraLabelToKey = new Dictionary<ushort, KeyCode>
        {
            { 64, KeyCode.Quote },
            { 25, KeyCode.BackQuote },
            /* SkyHookKeyToUnityKey misnames label 37 as Greater on Linux: a real '=' press
               (raw=0x3D, the X11 keysym for '=') comes back labeled 37, and the reverse
               table hands back KeyCode.Greater for it instead of KeyCode.Equals. UnityKeyToSkyHookKey
               agrees the forward direction is 37 for Equals, so only the reverse row is wrong —
               override it the same way Quote/BackQuote are overridden above. */
            { 37, KeyCode.Equals },
            // SkyHookKeyToUnityKey maps Semicolon (63) to KeyCode.Colon — the shifted symbol —
            // so ';' presses lit the wrong key-viewer cell and chatter-tracked as another key.
            { 63, KeyCode.Semicolon },
        };

        private static Dictionary<ushort, KeyCode> BuildVkMap()
        {
            var m = new Dictionary<ushort, KeyCode>
            {
                { 0x08, KeyCode.Backspace },
                { 0x09, KeyCode.Tab },
                { 0x0D, KeyCode.Return },
                { 0x14, KeyCode.CapsLock },
                { 0x1B, KeyCode.Escape },
                { 0x20, KeyCode.Space },
                { 0x21, KeyCode.PageUp },
                { 0x22, KeyCode.PageDown },
                { 0x23, KeyCode.End },
                { 0x24, KeyCode.Home },
                { 0x25, KeyCode.LeftArrow },
                { 0x26, KeyCode.UpArrow },
                { 0x27, KeyCode.RightArrow },
                { 0x28, KeyCode.DownArrow },
                { 0x2D, KeyCode.Insert },
                { 0x2E, KeyCode.Delete },
                { 0x5B, KeyCode.LeftCommand },   // VK_LWIN
                { 0x5C, KeyCode.RightCommand },  // VK_RWIN
                { 0x5D, KeyCode.Menu },          // VK_APPS — SkyHook has no label for it
                { 0x6A, KeyCode.KeypadMultiply },
                { 0x6B, KeyCode.KeypadPlus },
                { 0x6D, KeyCode.KeypadMinus },
                { 0x6E, KeyCode.KeypadPeriod },
                { 0x6F, KeyCode.KeypadDivide },
                { 0xA0, KeyCode.LeftShift },
                { 0xA1, KeyCode.RightShift },
                { 0xA2, KeyCode.LeftControl },
                { 0xA3, KeyCode.RightControl },
                { 0xA4, KeyCode.LeftAlt },
                { 0xA5, KeyCode.RightAlt },
                { 0xBA, KeyCode.Semicolon },     // VK_OEM_1
                { 0xBB, KeyCode.Equals },
                { 0xBC, KeyCode.Comma },
                { 0xBD, KeyCode.Minus },
                { 0xBE, KeyCode.Period },
                { 0xBF, KeyCode.Slash },         // VK_OEM_2
                { 0xC0, KeyCode.BackQuote },     // VK_OEM_3
                { 0xDB, KeyCode.LeftBracket },
                { 0xDC, KeyCode.Backslash },
                { 0xDD, KeyCode.RightBracket },
                { 0xDE, KeyCode.Quote },         // VK_OEM_7
            };
            for (int i = 0; i < 10; i++) m[(ushort)(0x30 + i)] = KeyCode.Alpha0 + i;
            for (int i = 0; i < 26; i++) m[(ushort)(0x41 + i)] = KeyCode.A + i;
            for (int i = 0; i < 10; i++) m[(ushort)(0x60 + i)] = KeyCode.Keypad0 + i;
            for (int i = 0; i < 12; i++) m[(ushort)(0x70 + i)] = KeyCode.F1 + i;
            return m;
        }

        private static void EnsureReflection()
        {
            if (_reflReady) return;

            var rdInput       = AccessTools.TypeByName("RDInput");
            _getStateKeys     = rdInput     != null ? AccessTools.Method(rdInput, "GetStateKeys")         : null;

            var anyKcType     = AccessTools.TypeByName("AnyKeyCode");
            _anyKcValue       = anyKcType   != null ? AccessTools.Field(anyKcType,  "value")              : null;
            if (_getStateKeys != null && anyKcType != null && _getStateKeys.GetParameters().Length == 2)
                _stateKeysBuf = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(anyKcType));

            _asyncKcType      = AccessTools.TypeByName("AsyncKeyCode");
            _asyncKcLabel     = _asyncKcType != null ? AccessTools.Field(_asyncKcType, "label")           : null;
            _asyncKcKey       = _asyncKcType != null ? AccessTools.Field(_asyncKcType, "key")             : null;

            // ADOFAI v3 moved the mapper to SkyHook.Unity.dll and renamed it + its methods:
            // AsyncKeyMapper → SkyHookKeyMapper, UnityKeyToAsyncKey → UnityKeyToSkyHookKey,
            // AsyncKeyToUnityKey → SkyHookKeyToUnityKey (signatures otherwise unchanged).
            var mapper        = AccessTools.TypeByName("SkyHook.SkyHookKeyMapper");
            _unityToAsync     = mapper      != null ? AccessTools.Method(mapper, "UnityKeyToSkyHookKey")  : null;
            _asyncToUnity     = mapper      != null ? AccessTools.Method(mapper, "SkyHookKeyToUnityKey")  : null;

            _bsType           = AccessTools.TypeByName("ButtonState");
            // Pre-box the three ButtonState values once, so the per-frame CollectStateKeys
            // path never boxes an enum (Enum.ToObject allocates).
            _stateDown        = _bsType     != null ? System.Enum.ToObject(_bsType, StateWentDown) : (object)StateWentDown;
            _stateWentUp      = _bsType     != null ? System.Enum.ToObject(_bsType, StateWentUp)   : (object)StateWentUp;
            _stateIsDown      = _bsType     != null ? System.Enum.ToObject(_bsType, StateIsDown)   : (object)StateIsDown;

            _keyLabelType     = AccessTools.TypeByName("SkyHook.KeyLabel");
            _labelToNative    = mapper      != null ? AccessTools.Method(mapper, "KeyLabelToNativeKeyCode") : null;

            /* Only platforms whose raw codes testers' diagnostics confirmed get a table. A
               wrong table misnames presses and hands the game's limiter codes no real press
               matches; no table fails open and keeps us off the delegation path. */
            var plat = Application.platform;
            _rawToKeyCode = plat == RuntimePlatform.WindowsPlayer || plat == RuntimePlatform.WindowsEditor ? BuildVkMap()
                          : plat == RuntimePlatform.OSXPlayer     || plat == RuntimePlatform.OSXEditor     ? _hidToKeyCode
                          : null;

            _reflReady = true;
        }

        private static System.Type _bsType;

        // SkyHook label → Unity KeyCode. `label` is the boxed KeyLabel read off the entry;
        // labelVal is its numeric value. KeyCode.None = the label names no key we know.
        // Both mappers are pure, so memoize: they sit on the per-frame press path, and a
        // reflected Invoke allocates its argument array every call.
        private static readonly Dictionary<ushort, KeyCode> _labelToKeyMemo = new Dictionary<ushort, KeyCode>();
        private static readonly Dictionary<KeyCode, ushort> _keyToLabelMemo = new Dictionary<KeyCode, ushort>();
        private static readonly object[] _oneArg = new object[1];

        private static KeyCode KeyFromLabel(object label, ushort labelVal)
        {
            if (_extraLabelToKey.TryGetValue(labelVal, out KeyCode extra)) return extra;
            if (labelVal == LabelIgnored || labelVal == LabelUnknown || _asyncToUnity == null)
                return KeyCode.None;
            if (_labelToKeyMemo.TryGetValue(labelVal, out KeyCode memo)) return memo;
            _oneArg[0] = label;
            var resolved = _asyncToUnity.Invoke(null, _oneArg);
            return _labelToKeyMemo[labelVal] = (KeyCode)(resolved == null ? 0 : System.Convert.ToInt32(resolved));
        }

        // Unity KeyCode → SkyHook label, or LabelUnknown when the key has none.
        private static ushort LabelFromKey(KeyCode k)
        {
            if (_extraKeyToLabel.TryGetValue(k, out ushort extra)) return extra;
            if (_unityToAsync == null) return LabelUnknown;
            if (_keyToLabelMemo.TryGetValue(k, out ushort memo)) return memo;
            _oneArg[0] = k;
            var lbl = _unityToAsync.Invoke(null, _oneArg);
            return _keyToLabelMemo[k] = lbl == null ? LabelUnknown : (ushort)System.Convert.ToInt32(lbl);
        }

        // Resolve one GetStateKeys entry to a Unity KeyCode: direct KeyCode, SkyHook
        // label mapping, then the raw-key fallback table. KeyCode.None = unresolvable.
        private static KeyCode ResolveEntry(object val)
        {
            if (val is KeyCode directKc) return directKc;
            if (_asyncKcType == null || val.GetType() != _asyncKcType || _asyncKcLabel == null)
                return KeyCode.None;

            object label = _asyncKcLabel.GetValue(val);
            if (label == null) return KeyCode.None;
            var byLabel = KeyFromLabel(label, (ushort)System.Convert.ToInt32(label));
            if (byLabel != KeyCode.None) return byLabel;
            if (_asyncKcKey != null && _rawToKeyCode != null)
            {
                ushort raw = (ushort)System.Convert.ToInt32(_asyncKcKey.GetValue(val));
                if (_rawToKeyCode.TryGetValue(raw, out KeyCode mapped)) return mapped;
            }
            return KeyCode.None;
        }

        /* Topaz's own key observation (rebind capture, KV rain/counting) reads the
           game's async press lists ALONGSIDE legacy Input polling: a Proton/X11 tester's
           diagnostics proved legacy Input.GetKeyDown is blind there while SkyHook keeps
           resolving every key. ButtonState: WentDown=0, WentUp=1, IsDown=2. */
        internal const int StateWentDown = 0;
        internal const int StateWentUp   = 1;
        internal const int StateIsDown   = 2;

        // Pre-boxed ButtonState for `state` (no per-call boxing). Falls back to WentDown.
        private static object BoxedState(int state)
        {
            if (state == StateWentUp) return _stateWentUp;
            if (state == StateIsDown) return _stateIsDown;
            return _stateDown;
        }

        /* RDInput.GetStateKeys: builds before the 2026-10 update RETURN the list
           (GetStateKeys(ButtonState)); since then it FILLS a caller-supplied one
           (GetStateKeys(List<AnyKeyCode>, ButtonState)). Calling the old shape on the new game
           threw TargetParameterCountException on every press — straight out of the GetMain
           postfix while the limiter was on, and silently emptying the key viewer's async lists. */
        private static IList InvokeGetStateKeys(object state)
        {
            if (_stateKeysBuf == null)
            {
                _stateArgs[0] = state;
                return _getStateKeys.Invoke(null, _stateArgs) as IList;
            }
            _stateKeysBuf.Clear();
            _stateArgs2[0] = _stateKeysBuf;
            _stateArgs2[1] = state;
            _getStateKeys.Invoke(null, _stateArgs2);
            return _stateKeysBuf;
        }

        internal static void CollectStateKeys(int state, HashSet<KeyCode> into)
        {
            EnsureReflection();
            if (_getStateKeys == null || _anyKcValue == null || _bsType == null) return;

            _inCount = true; // GetStateKeys re-enters GetMain; keep the postfix out of the way
            IList list;
            try   { list = InvokeGetStateKeys(BoxedState(state)); }
            catch { list = null; }
            finally { _inCount = false; }
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                object val = _anyKcValue.GetValue(list[i]);
                if (val == null) continue;
                var kc = ResolveEntry(val);
                if (kc != KeyCode.None) into.Add(kc);
            }
        }

        internal static void Apply(Settings settings)
        {
            // Re-arm the per-session press diagnostics on every apply, so a tester can
            // refresh the log window by touching any setting before pressing keys.
            _pressDiagLeft = 400;
            _failOpenLogged = false;
            _heldOffLogged = false;

            _active = settings.KeyLimiterEnabled;
            _blockWhileOpen = settings.BlockInputsWhileMenuOpen;
            _chatterActive = settings.ChatterBlockerEnabled;
            _chatterThresholdSec = Mathf.Max(0, settings.ChatterThresholdMs) / 1000f;
            _allowed.Clear();
            _allowedLabels.Clear();
            _ghosts.Clear();

            if (_chatterActive) EnsureReflection();
            // A list an older build wrote into the game's limiter goes back to the player.
            ReleaseGameLimiter();
            if (!_active) return;

            EnsureReflection();

            var source = settings.KeyLimiterUseKvKeys
                ? GetKvKeys(settings)
                : ParseKeys(settings.KeyLimiterCustomKeys);
            foreach (var k in source)
            {
                _allowed.Add(k);
                // Unknown is the mapper's "no idea" answer — adding it would make every
                // unnameable press match the allowed set.
                ushort lbl = LabelFromKey(k);
                if (lbl != LabelUnknown) _allowedLabels.Add(lbl);
            }

            // Fail-safe: an empty allowed set would block EVERY key — never a sensible
            // intent (a tester enabled custom-keys mode with nothing listed and bricked
            // gameplay). Treat it as limiter-off until keys exist.
            if (_allowed.Count == 0)
            {
                _active = false;
                TopazLog.Debug("KeyLimiter.Apply: allowed set is empty — limiter treated as disabled");
            }

            /* Ghost keys (hand preset only) are withheld from the game. That rides the limiter
               toggle — collected only while it's active — so "limiter off" means every key
               reaches the game. */
            if (_active && settings.Hand != null && settings.Hand.GhostKeysEnabled && settings.Hand.GhostKeys != null)
            {
                foreach (var tok in settings.Hand.GhostKeys)
                {
                    if (string.IsNullOrEmpty(tok) || tok == "None") continue;
                    if (KeyViewer.TryParseKey(tok, out KeyCode kc)) _ghosts.Add(kc);
                }
            }

            // Apply fires on every settings notify (incl. per-tick slider drags) — only
            // log when the effective state actually changed.
            string state = $"KeyLimiter.Apply: enabled={_active} useKv={settings.KeyLimiterUseKvKeys} hand={(settings.Hand?.Name ?? "<null>")} foot={(settings.Foot?.Name ?? "<null>")} allowed=[{string.Join(",", _allowed)}] labels={_allowedLabels.Count} ghosts=[{string.Join(",", _ghosts)}]";
            if (state != _lastApplyLog)
            {
                _lastApplyLog = state;
                TopazLog.Debug(state);
            }
        }

        private static string _lastApplyLog;

        /* The game's own limiter: Persistence.keyLimiterKeys, filtered inside the RDInput
           keyboard types' Main. Older builds delegated to it by writing our keys there, but
           two limiters over two lists blocked keys the player had allowed, and SkyHook can't
           name many keys on some platforms (none at all on Linux). Topaz now always filters
           itself, and UseKeyLimiterPatch holds the game's limiter off while ours is on. What
           remains here hands back a list an older build took over (GameLimiterOwned). */
        private static bool _gameRefl;
        private static object _keysSetting;                 // Persistence.keyLimiterKeys
        private static MethodInfo _ksClear, _ksAddKey, _ksAddKeyRaw;

        private static void EnsureGameRefl()
        {
            if (_gameRefl) return;
            _gameRefl = true;
            try
            {
                var persistence = AccessTools.TypeByName("Persistence");
                var field = persistence != null ? AccessTools.Field(persistence, "keyLimiterKeys") : null;
                _keysSetting = field?.GetValue(null);
                if (_keysSetting == null) return;
                var t = _keysSetting.GetType();
                _ksClear = AccessTools.Method(t, "Clear");
                _ksAddKey = AccessTools.Method(t, "Add", new[] { typeof(KeyCode) });
                // Two-arg overload: explicit native code for keys SkyHook cannot name.
                _ksAddKeyRaw = AccessTools.Method(t, "Add", new[] { typeof(KeyCode), typeof(ushort?) });
            }
            catch (Exception e)
            {
                _keysSetting = null;
                TopazLog.Log("KeyLimiter: game key limiter unavailable (" + e.Message + ")");
            }
        }

        private static HashSet<KeyCode> ParseKeyList(string csv)
        {
            var set = new HashSet<KeyCode>();
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (var part in csv.Split(','))
                if (System.Enum.TryParse(part.Trim(), out KeyCode kc)) set.Add(kc);
            return set;
        }

        private static void WriteGameKeys(IEnumerable<KeyCode> keys)
        {
            _ksClear.Invoke(_keysSetting, null);
            var args = new object[1];
            var args2 = new object[2];
            foreach (var k in keys)
            {
                /* Two-arg Add with the native code WE resolved: Add(KeyCode) re-derives it
                   through the mapper, which has no row for ' or ` and would store the 0xFFFF
                   sentinel — the game's async filter then blocks a key the player listed. */
                ushort native = NativeForKey(k);
                if (_ksAddKeyRaw == null)
                {
                    args[0] = k;
                    _ksAddKey.Invoke(_keysSetting, args);
                    continue;
                }
                args2[0] = k;
                args2[1] = native == NativeNone ? (ushort?)null : native;   // null = unityKeys only (mouse)
                _ksAddKeyRaw.Invoke(_keysSetting, args2);
            }
        }

        // Reverse of the platform raw-key table, for keys SkyHook can't name.
        private static Dictionary<KeyCode, ushort> _keyToRaw;

        private static bool RawForKey(KeyCode k, out ushort raw)
        {
            raw = 0;
            EnsureReflection();
            if (_rawToKeyCode == null) return false;
            if (_keyToRaw == null)
            {
                _keyToRaw = new Dictionary<KeyCode, ushort>();
                foreach (var kv in _rawToKeyCode) _keyToRaw[kv.Value] = kv.Key;
            }
            return _keyToRaw.TryGetValue(k, out raw);
        }

        // KeysSetting.Add's "no async code" sentinel — it stores the Unity key only for these.
        private const ushort NativeNone = 0xFFFF;

        /* The raw code the game's async filter compares presses against: SkyHook's own
           platform converter, fed OUR label. Same call the game makes internally, minus its
           two missing mapper rows. NativeNone = this key can't be expressed. */
        private static ushort NativeForKey(KeyCode k)
        {
            EnsureReflection();
            ushort label = LabelFromKey(k);
            if (label != LabelUnknown && label != LabelIgnored && _labelToNative != null && _keyLabelType != null)
            {
                try
                {
                    var code = _labelToNative.Invoke(null, new[] { System.Enum.ToObject(_keyLabelType, label) });
                    ushort native = code == null ? NativeNone : (ushort)System.Convert.ToInt32(code);
                    if (native != 0 && native != NativeNone) return native;
                }
                catch { }
            }
            // Keys SkyHook itself can't name (Menu/Apps) — our own table, on the two platforms
            // that have one.
            return RawForKey(k, out ushort raw) ? raw : NativeNone;
        }

        /* Restore the player's own list if an older build left ours in the game's limiter.
           An EMPTY list disables the game's limiter (filter gated on Count > 0). Mod unload /
           master switch off calls this too, so our keys never outlive the mod. */
        internal static void ReleaseGameLimiter()
        {
            var s = MainClass.Settings;
            if (s == null || !s.GameLimiterOwned) return;
            EnsureGameRefl();
            if (_keysSetting == null || _ksClear == null || _ksAddKey == null) return;
            try
            {
                var restore = ParseKeyList(s.GameLimiterUserKeys);
                WriteGameKeys(restore);
                s.GameLimiterOwned = false;
                s.GameLimiterUserKeys = "";
                MainClass.PersistNow();
                TopazLog.Log($"KeyLimiter: released the game's limiter, restored {restore.Count} player key(s)");
            }
            catch (Exception e)
            {
                TopazLog.Log("KeyLimiter: game limiter release failed: " + e);
            }
        }

        private static IEnumerable<KeyCode> GetKvKeys(Settings settings)
        {
            foreach (var kc in PresetKeys(settings.Hand)) yield return kc;
            foreach (var kc in PresetKeys(settings.Foot)) yield return kc;
        }

        private static IEnumerable<KeyCode> PresetKeys(KeyViewerPreset preset)
        {
            if (preset?.Rows == null) yield break;
            foreach (var row in preset.Rows)
            {
                if (row == null) continue;
                row.EnsureDefaults();
                if (row.Cells == null) continue;
                foreach (var cell in row.Cells)
                {
                    string tok = cell.Token;
                    if (tok == "KPS" || tok == "Total") continue;
                    if (KeyViewer.TryParseKey(tok, out KeyCode kc))
                        yield return kc;
                }
            }
        }

        private static IEnumerable<KeyCode> ParseKeys(string input)
        {
            foreach (var tok in SplitKeyTokens(input))
            {
                if (KeyViewer.TryParseKey(tok, out KeyCode kc))
                    yield return kc;
            }
        }

        /* Custom key lists are space-separated. ',' is a key token in its own right, so it
           can't also be a separator (splitting on it silently dropped the Comma key); a
           trailing comma on a longer token is still trimmed, for lists written "Tab, 1, 2". */
        internal static List<string> SplitKeyTokens(string input)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return list;
            foreach (var raw in input.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.Length > 1 ? raw.TrimEnd(',') : raw;
                if (tok.Length > 0) list.Add(tok);
            }
            return list;
        }

        // Presses in the game's own list (GetStateKeys) this frame that pass our filters.
        // Idempotent within a frame (chatter decisions are frame-cached); entries we can't
        // name pass through untouched.
        private static bool _inCount;
        private static bool _failOpenLogged;
        private static int _pressDiagLeft = 16; // one-time per-session press dump for ports
        private static int CountAllowedInPressedKeys()
        {
            EnsureReflection();
            if (_getStateKeys == null || _anyKcValue == null) return 0;

            _inCount = true;
            IList list;
            try   { list = InvokeGetStateKeys(_stateDown); }
            finally { _inCount = false; }

            if (list == null) return 0;

            if (_chatterActive && _chatterFrame != Time.frameCount)
            {
                _chatterFrame = Time.frameCount;
                _chatterDecisionThisFrame.Clear();
            }
            float now = _chatterActive ? Time.realtimeSinceStartup : 0f;

            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                object val = _anyKcValue.GetValue(list[i]);
                if (val == null) continue;

                // Resolve press entry → (resolvedKey, isMouse, allowedByLimiter).
                KeyCode resolvedKey = KeyCode.None;
                bool isMouse = false;
                bool allowed = false;

                if (val is KeyCode directKc)
                {
                    resolvedKey = directKc;
                    int ki = (int)directKc;
                    isMouse = (ki >= (int)KeyCode.Mouse0 && ki <= (int)KeyCode.Mouse6);
                    allowed = isMouse || _allowed.Contains(directKc);   // diagnostics only
                    if (_pressDiagLeft > 0)
                    {
                        _pressDiagLeft--;
                        TopazLog.Debug($"KeyLimiter press: direct kc={directKc} allowed={allowed}");
                    }
                }
                else if (_asyncKcType != null && val.GetType() == _asyncKcType && _asyncKcLabel != null)
                {
                    object label = _asyncKcLabel.GetValue(val);
                    if (label == null) continue;
                    ushort labelVal = (ushort)System.Convert.ToInt32(label);

                    resolvedKey = KeyFromLabel(label, labelVal);
                    allowed = _allowedLabels.Contains(labelVal);

                    // Raw-key fallback (platform table) for label=Unknown entries.
                    ushort rawKey = 0;
                    if (_asyncKcKey != null)
                        rawKey = (ushort)System.Convert.ToInt32(_asyncKcKey.GetValue(val));
                    if (resolvedKey == KeyCode.None && rawKey != 0 && _rawToKeyCode != null
                        && _rawToKeyCode.TryGetValue(rawKey, out KeyCode mapped))
                    {
                        resolvedKey = mapped;
                    }

                    // Label mismatch tolerance: if the native bundle's label for a key
                    // differs from what UnityKeyToSkyHookKey predicted (seen per-platform),
                    // the label check fails even though we KNOW the key — trust the
                    // resolved identity when it's in the allowed set.
                    if (!allowed && resolvedKey != KeyCode.None)
                        allowed = _allowed.Contains(resolvedKey);

                    if (_pressDiagLeft > 0)
                    {
                        _pressDiagLeft--;
                        TopazLog.Debug($"KeyLimiter press: async label={labelVal} raw=0x{rawKey:X2} resolved={resolvedKey} allowed={allowed}");
                    }
                }

                // Ghost filter — always applies. Ghost-key presses are never input to the game.
                if (resolvedKey != KeyCode.None && _ghosts.Contains(resolvedKey)) continue;

                // Fail open per ENTRY (not per frame — that let every key pressed alongside
                // an unnameable one through).
                if (resolvedKey == KeyCode.None)
                {
                    n++;
                    if (!_failOpenLogged)
                    {
                        _failOpenLogged = true;
                        TopazLog.Log("KeyLimiter: a press entry is unrecognized on this platform — it bypasses the limiter/chatter blocker. Please report with the [dbg] 'KeyLimiter press' lines.");
                    }
                    continue;
                }

                if (_active && !allowed) continue;

                // Chatter filter — skip mouse, skip entries we couldn't resolve to a KeyCode.
                if (_chatterActive && !isMouse && resolvedKey != KeyCode.None)
                {
                    bool isChatter;
                    if (_chatterDecisionThisFrame.TryGetValue(resolvedKey, out bool cached))
                    {
                        isChatter = cached;
                    }
                    else
                    {
                        isChatter = _lastPressTime.TryGetValue(resolvedKey, out float last)
                                    && (now - last) < _chatterThresholdSec;
                        if (!isChatter) _lastPressTime[resolvedKey] = now;
                        _chatterDecisionThisFrame[resolvedKey] = isChatter;
                    }
                    if (isChatter) continue;
                }

                n++;
            }

            // Escape always passes (handled as special input by the game, not in press list)
            if (Input.GetKeyDown(KeyCode.Escape)) n++;

            // P / Space pass outside active play (death screen, pause menu, between tiles).
            // PlayerControl is the only state where the game is actively reading gameplay input.
            var sc = scrController.instance;
            bool playing = sc != null && sc.state == States.PlayerControl;
            if (!playing && (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Space)))
                n++;

            return n;
        }

        // Menu-open input block. The game reads the keyboard through three RDInput entry
        // points (GetMain, WentDown/IsDown, GetState) plus raw Input.GetKeyDown in menus; each
        // gets its own gate below. The panel polls UnityEngine.Input itself, so it stays live.
        // Autoplay is exempt: it drives hits through the same pipeline, and blocking it
        // starved the hit tracker (empty counts / NaN accuracy).
        private static bool BlockInputs => _blockWhileOpen && UICore.IsOpen && !Autoplaying;

        /// Same question for PrismLib: while this is true, every other mod's hotkeys are dead and
        /// they have no way to know it from their own side. MainClass turns it into a claim.
        internal static bool BlockingInputs => BlockInputs;

        private static bool Autoplaying
        {
            get { try { return RDC.auto; } catch { return false; } }
        }

        // ── RDInput.GetMain — platform-agnostic aggregator ─────────────────
        [HarmonyPatch]
        private static class GetMainPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("RDInput");
                return t != null ? AccessTools.Method(t, "GetMain") : null;
            }

            public static void Postfix(ButtonState __0, ref int __result)
            {
                if (BlockInputs && __0 == ButtonState.WentDown) { __result = 0; return; }
                // Counting reflects over the press list; only worth it while WE still filter
                // something (limiter, chatter, ghosts). Also skips re-entry — GetStateKeys
                // calls GetMain internally.
                bool weStillFilter = _active || _chatterActive || _ghosts.Count > 0;
                if (!weStillFilter || __result == 0 || __0 != ButtonState.WentDown || _inCount) return;

                // Fail open: a game update that moves the reflected input API must cost us the
                // filter, never the player's input (an exception here propagates out of GetMain).
                try { __result = Mathf.Min(__result, CountAllowedInPressedKeys()); }
                catch (Exception e)
                {
                    if (_countFailLogged) return;
                    _countFailLogged = true;
                    TopazLog.Log("KeyLimiter: press filter failed, passing input through — " + (e.InnerException ?? e));
                }
            }

            private static bool _countFailLogged;
        }

        // ── RDInput.useKeyLimiter — the game's own limiter gate ────────────
        // Never both limiters: while ours is on, the game's is off whatever the player's own
        // list holds. Its only readers are the keyboard input types' Main/MainIgnoreActive.
        [HarmonyPatch]
        private static class UseKeyLimiterPatch
        {
            // 3.x only: 2.9 has no limiter of its own to hold off.
            static bool Prepare() => TargetMethod() != null;

            static MethodBase TargetMethod()
                => typeof(scrController).Assembly.GetType("RDInput")?.GetProperty("useKeyLimiter",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)?.GetGetMethod(true);

            public static void Postfix(ref bool __result)
            {
                if (!_active || !__result) return;
                __result = false;
                if (_heldOffLogged) return;
                _heldOffLogged = true;
                TopazLog.Debug("KeyLimiter: game's own limiter held off (Topaz's limiter is on)");
            }
        }

        private static bool _heldOffLogged;

        // ── RDInput.WentDown / IsDown — raw keyboard shortcut reads ────────
        [HarmonyPatch]
        private static class WentDownBlockPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("RDInput");
                return t != null ? AccessTools.Method(t, "WentDown") : null;
            }

            public static void Postfix(ref bool __result)
            {
                if (BlockInputs) __result = false;
            }
        }

        [HarmonyPatch]
        private static class IsDownBlockPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("RDInput");
                return t != null ? AccessTools.Method(t, "IsDown") : null;
            }

            public static void Postfix(ref bool __result)
            {
                if (BlockInputs) __result = false;
            }
        }

        // RDInput.GetState — Rewired action reads (restart/back/confirm/…)
        [HarmonyPatch]
        private static class GetStateBlockPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("RDInput");
                return t != null ? AccessTools.Method(t, "GetState") : null;
            }

            public static void Postfix(ref bool __result)
            {
                if (BlockInputs) __result = false;
            }
        }

        // Menu scenes read number keys straight off Input.GetKeyDown (an extern icall), so it's
        // patched outside PatchAll — a failed native detour loses only this layer. PatchProcessor
        // rather than harmony.Patch(…): that overload binds to HarmonyX 2.10's 6-arg signature,
        // which native UMM's older 0Harmony lacks (MissingMethodException at JIT, whole mod dead).
        internal static void TryPatchRawInput(Harmony harmony)
        {
            try
            {
                var m = AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) });
                var proc = harmony.CreateProcessor(m);
                proc.AddPostfix(new HarmonyMethod(typeof(KeyLimiter), nameof(GetKeyDownPostfix)));
                proc.Patch();
            }
            catch (System.Exception e)
            {
                TopazLog.Log("Input.GetKeyDown patch failed (menu keys won't be blocked): " + e.Message);
            }
        }

        // Topaz's own pollers (rebind/limiter KeyListeners, KV rain & counting) must
        // keep seeing keys while the menu is open — they set this around their reads.
        // Unity's main loop is single-threaded, so a plain flag is safe.
        internal static bool RawReadExempt;

        /* Alt is the Prism modifier and the game does not use it, so an Alt chord reads through
           the block. Without this the shared debug and settings windows are unreachable from the
           moment Topaz's own panel is open — including for Topaz's own poll, since the postfix
           does not care who is asking. That is what made Alt+D look like a broken keybind. */
        private static bool PrismChordHeld =>
            Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);

        // KeyCode.T stays readable so Ctrl+T still closes the panel.
        private static void GetKeyDownPostfix(KeyCode key, ref bool __result)
        {
            if (__result && !RawReadExempt && key != KeyCode.T && !PrismChordHeld && BlockInputs) __result = false;
        }

        /* While the menu is open, no hits register — same gate as the GetMain block. The
           limiter itself is GetMain's job alone: the second pass that used to sit here judged
           over legacy UnityEngine.Input, which lags the async keyboard or misreads it (native
           Wayland with an IME let only digits through), so all it ever did was drop hits. */
        [HarmonyPatch]
        private static class AddHitBlockPatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(GameCompat.TrackerType, "AddHit", new[] { typeof(HitMargin) });

            public static bool Prefix() => !BlockInputs;
        }
    }
}
