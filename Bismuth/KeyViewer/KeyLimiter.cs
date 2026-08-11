using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Bismuth.UI;
using HarmonyLib;
using UnityEngine;

namespace Bismuth
{
    internal static class KeyLimiter
    {
        private static readonly HashSet<KeyCode> _allowed = new HashSet<KeyCode>();
        private static bool _active;

        // Block all game inputs while the Bismuth menu is open.
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
        private static MethodInfo _getStateKeys;      // RDInput.GetStateKeys(ButtonState) → List<AnyKeyCode>
        private static FieldInfo  _anyKcValue;        // AnyKeyCode.value
        private static System.Type _asyncKcType;      // AsyncKeyCode
        private static FieldInfo  _asyncKcLabel;      // AsyncKeyCode.label (SkyHook.KeyLabel)
        private static FieldInfo  _asyncKcKey;        // AsyncKeyCode.key (ushort raw OS scancode)
        private static MethodInfo _unityToAsync;      // SkyHook.SkyHookKeyMapper.UnityKeyToSkyHookKey(KeyCode) → KeyLabel
        private static MethodInfo _asyncToUnity;      // SkyHook.SkyHookKeyMapper.SkyHookKeyToUnityKey(KeyLabel) → KeyCode
        private static object     _stateDown;         // ButtonState.WentDown (enum value 0)
        private static object     _stateWentUp;       // ButtonState.WentUp (1)
        private static object     _stateIsDown;       // ButtonState.IsDown (2)
        /* Reused single-element args buffer for the per-frame GetStateKeys.Invoke (called
           2-3× every frame while the key viewer runs). Reflection Invoke otherwise
           allocates a fresh object[] each call — steady GC pressure during play. Safe to
           share: all reads are main-thread and sequential, and the _inCount guard keeps the
           GetMain postfix from re-entering GetStateKeys mid-Invoke. */
        private static readonly object[] _stateArgs = new object[1];

        // Pre-computed set of allowed SkyHook KeyLabel values (ushort) for async keyboard path.
        // Built from _allowed via UnityKeyToSkyHookKey so we compare labels directly,
        // avoiding the ambiguity in SkyHookKeyToUnityKey (multiple KeyCodes share one label slot).
        private static readonly HashSet<ushort> _allowedLabels = new HashSet<ushort>();

        // Raw-key → Unity KeyCode fallback for when SkyHook reports KeyLabel.Unknown.
        // The raw byte's meaning is PER PLATFORM and the spaces collide (0x39 = HID
        // CapsLock vs VK '9'), so EnsureReflection picks the table:
        //  - macOS native bundle: USB HID usage IDs (page 0x07) — modifiers confirmed
        //    via diagnostic logging (0xE1 LShift, 0xE5 RShift, …).
        //  - Windows build (incl. Proton): Win32 VK codes — confirmed via a Proton
        //    tester's diagnostics (0x42 'B', 0xA2 VK_LCONTROL, …).
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
        };

        // SkyHook.KeyLabel: 119 = IgnoredInternal, 120 = Unknown. Neither names a key, and
        // UnityKeyToSkyHookKey returns Unknown for anything it can't map.
        private const ushort LabelIgnored = 119;
        private const ushort LabelUnknown = 120;

        /* SkyHookKeyMapper's table has no entry for ' or ` — UnityKeyToSkyHookKey returns
           Unknown for both and SkyHookKeyToUnityKey can't map their labels back. Two ways
           that broke the limiter: an allowed ' / ` (the shipped 16k preset binds ` to the
           Caps slot) put Unknown into _allowedLabels, so EVERY press the mapper couldn't
           name counted as allowed; and their own presses resolved to nothing. Supply the
           missing pairs ourselves — the labels exist, only the mapper's rows are absent. */
        private static readonly Dictionary<KeyCode, ushort> _extraKeyToLabel = new Dictionary<KeyCode, ushort>
        {
            { KeyCode.Quote,     64 },  // KeyLabel.Apostrophe
            { KeyCode.BackQuote, 25 },  // KeyLabel.Grave
        };
        private static readonly Dictionary<ushort, KeyCode> _extraLabelToKey = new Dictionary<ushort, KeyCode>
        {
            { 64, KeyCode.Quote },
            { 25, KeyCode.BackQuote },
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

            var plat = Application.platform;
            _rawToKeyCode = plat == RuntimePlatform.WindowsPlayer || plat == RuntimePlatform.WindowsEditor
                ? BuildVkMap()
                : _hidToKeyCode;

            _reflReady = true;
        }

        private static System.Type _bsType;

        // SkyHook label → Unity KeyCode. `label` is the boxed KeyLabel read off the entry;
        // labelVal is its numeric value. KeyCode.None = the label names no key we know.
        private static KeyCode KeyFromLabel(object label, ushort labelVal)
        {
            if (_extraLabelToKey.TryGetValue(labelVal, out KeyCode extra)) return extra;
            if (labelVal == LabelIgnored || labelVal == LabelUnknown || _asyncToUnity == null)
                return KeyCode.None;
            var resolved = _asyncToUnity.Invoke(null, new object[] { label });
            if (resolved == null) return KeyCode.None;
            int kc = System.Convert.ToInt32(resolved);
            return kc == (int)KeyCode.None ? KeyCode.None : (KeyCode)kc;
        }

        // Unity KeyCode → SkyHook label, or LabelUnknown when the key has none.
        private static ushort LabelFromKey(KeyCode k)
        {
            if (_extraKeyToLabel.TryGetValue(k, out ushort extra)) return extra;
            if (_unityToAsync == null) return LabelUnknown;
            var lbl = _unityToAsync.Invoke(null, new object[] { k });
            return lbl == null ? LabelUnknown : (ushort)System.Convert.ToInt32(lbl);
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
            if (_asyncKcKey != null)
            {
                ushort raw = (ushort)System.Convert.ToInt32(_asyncKcKey.GetValue(val));
                if (_rawToKeyCode.TryGetValue(raw, out KeyCode mapped)) return mapped;
            }
            return KeyCode.None;
        }

        /* Bismuth's own key observation (rebind capture, KV rain/counting) reads the
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

        internal static void CollectStateKeys(int state, HashSet<KeyCode> into)
        {
            EnsureReflection();
            if (_getStateKeys == null || _anyKcValue == null || _bsType == null) return;

            _inCount = true; // GetStateKeys re-enters GetMain; keep the postfix out of the way
            IList list;
            try   { _stateArgs[0] = BoxedState(state); list = _getStateKeys.Invoke(null, _stateArgs) as IList; }
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
            _pressDiagLeft = 24;
            _failOpenLogged = false;

            _active = settings.KeyLimiterEnabled;
            _blockWhileOpen = settings.BlockInputsWhileMenuOpen;
            _chatterActive = settings.ChatterBlockerEnabled;
            _chatterThresholdSec = Mathf.Max(0, settings.ChatterThresholdMs) / 1000f;
            _allowed.Clear();
            _allowedLabels.Clear();
            _ghosts.Clear();

            if (_chatterActive) EnsureReflection();
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
                BismuthLog.Debug("KeyLimiter.Apply: allowed set is empty — limiter treated as disabled");
            }

            /* Ghost keys (active hand preset only — foot has none) spawn rain without hitting
               a tile, so suppressing them is a second way Bismuth withholds presses from the
               game. It rides the limiter toggle: collected only once the limiter is confirmed
               active, so "limiter off" means every key reaches the game. Before this, a preset
               with ghost keys ate them with the limiter disabled and nothing in the Input tab
               could stop it. */
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
                BismuthLog.Debug(state);
            }
        }

        private static string _lastApplyLog;

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
            if (string.IsNullOrWhiteSpace(input)) yield break;
            foreach (var tok in input.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (KeyViewer.TryParseKey(tok.Trim(), out KeyCode kc))
                    yield return kc;
            }
        }

        // Counts keys in the game's press list this frame that pass our filters (KeyLimiter
        // allowed-set + ChatterBlocker). Uses GetStateKeys (the game's own source, immune to
        // async timing) and is idempotent within a frame (chatter state is frame-cached).
        // Entries we can't name pass through untouched (platform key tables differ; a Linux
        // tester had every press eaten because SkyHook labels didn't match ours).
        private static bool _inCount;
        private static bool _failOpenLogged;
        private static int _pressDiagLeft = 16; // one-time per-session press dump for ports
        private static int CountAllowedInPressedKeys()
        {
            EnsureReflection();
            if (_getStateKeys == null || _anyKcValue == null) return 0;

            _inCount = true;
            IList list;
            try   { _stateArgs[0] = _stateDown; list = _getStateKeys.Invoke(null, _stateArgs) as IList; }
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
                    allowed = isMouse || _allowed.Contains(directKc);
                    if (_pressDiagLeft > 0)
                    {
                        _pressDiagLeft--;
                        BismuthLog.Debug($"KeyLimiter press: direct kc={directKc} allowed={allowed}");
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
                    if (resolvedKey == KeyCode.None && rawKey != 0
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
                        BismuthLog.Debug($"KeyLimiter press: async label={labelVal} raw=0x{rawKey:X2} resolved={resolvedKey} allowed={allowed}");
                    }
                }

                // Ghost filter — always applies. Ghost-key presses are never input to the game.
                if (resolvedKey != KeyCode.None && _ghosts.Contains(resolvedKey)) continue;

                /* Fail open, per ENTRY: this platform's label/raw tables don't name this key,
                   so filtering it would eat an input we can't identify. Was a whole-frame
                   bail-out, which meant one unnameable key (' and ` resolved to nothing until
                   _extraLabelToKey existed) let every key pressed alongside it through too. */
                if (resolvedKey == KeyCode.None)
                {
                    n++;
                    if (!_failOpenLogged)
                    {
                        _failOpenLogged = true;
                        BismuthLog.Log("KeyLimiter: a press entry is unrecognized on this platform — it bypasses the limiter/chatter blocker. Please report with the [dbg] 'KeyLimiter press' lines.");
                    }
                    continue;
                }

                // Limiter filter
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

        // While the menu is open the game must not see keyboard input. It reads the keyboard
        // through three independent RDInput entry points, each needing its own gate:
        //   GetMain(ButtonState)            — press counting → planet hits
        //   WentDown/IsDown(KeyCode)        — raw shortcut keys (R restart, arrows, …)
        //   GetState(InputAction, state)    — Rewired actions (restartPress, backPress, …)
        // The settings panel polls UnityEngine.Input directly (Ctrl+B, text fields), so it
        // stays responsive while all of these return "nothing pressed".
        //
        // Autoplay is EXEMPT: the game drives an autoplay run through this same input
        // pipeline (PlayerControl_Update → planet hits), so blocking it starved the hit
        // tracker — the results showed empty counts / NaN accuracy when the panel was open
        // during an autoplay run. The player isn't hitting tiles manually then, so there's
        // nothing to block.
        private static bool BlockInputs => _blockWhileOpen && UICore.IsOpen && !Autoplaying;

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
                // Skip when re-entering (GetStateKeys calls GetMain internally)
                if ((!_active && !_chatterActive && _ghosts.Count == 0) || __result == 0 || __0 != ButtonState.WentDown || _inCount) return;

                __result = Mathf.Min(__result, CountAllowedInPressedKeys());
            }
        }

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

        // ── UnityEngine.Input.GetKeyDown — direct polls below RDInput ──────
        // Menu scenes read number-key navigation straight off Input.GetKeyDown, bypassing
        // RDInput. GetKeyDown is an extern icall, so it's patched outside PatchAll: if the
        // native detour fails, only this layer is lost instead of aborting every patch.
        //
        // Applied via PatchProcessor rather than harmony.Patch(m, postfix: …): the latter binds
        // (at build time, against MelonLoader's HarmonyX 2.10) to the 6-arg Patch overload with
        // the extra `ilmanipulator` HarmonyMethod, which native UMM's older 0Harmony lacks — so
        // the method failed to JIT and threw MissingMethodException before the try even ran,
        // aborting the whole mod load on Windows/native UMM. CreateProcessor/AddPostfix/Patch
        // have been stable since Harmony 2.0.
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
                BismuthLog.Log("Input.GetKeyDown patch failed (menu keys won't be blocked): " + e.Message);
            }
        }

        // Bismuth's own pollers (rebind/limiter KeyListeners, KV rain & counting) must
        // keep seeing keys while the menu is open — they set this around their reads.
        // Unity's main loop is single-threaded, so a plain flag is safe.
        internal static bool RawReadExempt;

        // KeyCode.B stays readable so Ctrl+B still closes the panel.
        private static void GetKeyDownPostfix(KeyCode key, ref bool __result)
        {
            if (__result && !RawReadExempt && key != KeyCode.B && BlockInputs) __result = false;
        }

        // Fallback: block accuracy recording for non-allowed key presses
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit", new[] { typeof(HitMargin) })]
        private static class AddHitBlockPatch
        {
            public static bool Prefix()
            {
                // While the menu is open, no hits register — same gate as the GetMain block.
                if (BlockInputs) return false;
                if (!_active) return true;
                if (!Input.anyKeyDown) return true;
                // GetKey (not GetKeyDown) tolerates the 1-frame async delay here
                foreach (var key in _allowed)
                    if (Input.GetKey(key)) return true;
                for (int m = (int)KeyCode.Mouse0; m <= (int)KeyCode.Mouse6; m++)
                    if (Input.GetKeyDown((KeyCode)m)) return true;
                if (Input.GetKey(KeyCode.Escape)) return true;
                var sc = scrController.instance;
                bool playing = sc != null && sc.gameworld && !sc.paused;
                return !playing && (Input.GetKey(KeyCode.P) || Input.GetKey(KeyCode.Space));
            }
        }
    }
}
