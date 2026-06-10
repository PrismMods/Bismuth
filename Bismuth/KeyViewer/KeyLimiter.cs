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
        private static MethodInfo _unityToAsync;      // SkyHook.AsyncKeyMapper.UnityKeyToAsyncKey(KeyCode) → KeyLabel
        private static MethodInfo _asyncToUnity;      // SkyHook.AsyncKeyMapper.AsyncKeyToUnityKey(KeyLabel) → KeyCode
        private static object     _stateDown;         // ButtonState.Down (enum value 0)

        // Pre-computed set of allowed SkyHook KeyLabel values (ushort) for async keyboard path.
        // Built from _allowed via UnityKeyToAsyncKey so we compare labels directly,
        // avoiding the ambiguity in AsyncKeyToUnityKey (multiple KeyCodes share one label slot).
        private static readonly HashSet<ushort> _allowedLabels = new HashSet<ushort>();

        // Raw HID Usage ID → Unity KeyCode fallback for when SkyHook reports KeyLabel.Unknown.
        // Native bundle uses USB HID keyboard usage codes (page 0x07). Observed values for modifiers:
        // 0xE1 LShift, 0xE5 RShift, etc. — confirmed via diagnostic logging.
        private static readonly Dictionary<ushort, KeyCode> _hidToKeyCode = new Dictionary<ushort, KeyCode>
        {
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

            var mapper        = AccessTools.TypeByName("SkyHook.AsyncKeyMapper");
            _unityToAsync     = mapper      != null ? AccessTools.Method(mapper, "UnityKeyToAsyncKey")    : null;
            _asyncToUnity     = mapper      != null ? AccessTools.Method(mapper, "AsyncKeyToUnityKey")    : null;

            var bsType        = AccessTools.TypeByName("ButtonState");
            _stateDown        = bsType      != null ? System.Enum.ToObject(bsType, 0) : (object)0;

            _reflReady = true;
        }

        internal static void Apply(Settings settings)
        {
            _active = settings.KeyLimiterEnabled;
            _blockWhileOpen = settings.BlockInputsWhileMenuOpen;
            _chatterActive = settings.ChatterBlockerEnabled;
            _chatterThresholdSec = Mathf.Max(0, settings.ChatterThresholdMs) / 1000f;
            _allowed.Clear();
            _allowedLabels.Clear();
            _ghosts.Clear();

            // Collect ghost keys from the active hand preset (foot doesn't support ghosts).
            if (settings.Hand != null && settings.Hand.GhostKeysEnabled && settings.Hand.GhostKeys != null)
            {
                foreach (var tok in settings.Hand.GhostKeys)
                {
                    if (string.IsNullOrEmpty(tok) || tok == "None") continue;
                    if (KeyViewer.TryParseKey(tok, out KeyCode kc)) _ghosts.Add(kc);
                }
            }

            if (_chatterActive || _ghosts.Count > 0) EnsureReflection();
            if (!_active) return;

            EnsureReflection();

            var source = settings.KeyLimiterUseKvKeys
                ? GetKvKeys(settings)
                : ParseKeys(settings.KeyLimiterCustomKeys);
            foreach (var k in source)
            {
                _allowed.Add(k);
                if (_unityToAsync != null)
                {
                    var lbl = _unityToAsync.Invoke(null, new object[] { k });
                    if (lbl != null)
                        _allowedLabels.Add((ushort)System.Convert.ToInt32(lbl));
                }
            }

            BismuthLog.Log($"KeyLimiter.Apply: enabled={_active} useKv={settings.KeyLimiterUseKvKeys} hand={(settings.Hand?.Name ?? "<null>")} foot={(settings.Foot?.Name ?? "<null>")} allowed=[{string.Join(",", _allowed)}] labels={_allowedLabels.Count}");
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
            if (string.IsNullOrWhiteSpace(input)) yield break;
            foreach (var tok in input.Split(new[] { ' ', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (KeyViewer.TryParseKey(tok.Trim(), out KeyCode kc))
                    yield return kc;
            }
        }

        // Counts how many keys in the game's own press list this frame pass our filters.
        // Filters (any combination): KeyLimiter (allowed set), ChatterBlocker (within-threshold rejection).
        // Uses GetStateKeys (same source the game uses) — immune to async timing issues.
        // Idempotent across multiple calls in the same frame (chatter state is frame-cached).
        private static bool _inCount;
        private static int CountAllowedInPressedKeys()
        {
            EnsureReflection();
            if (_getStateKeys == null || _anyKcValue == null) return 0;

            _inCount = true;
            IList list;
            try   { list = _getStateKeys.Invoke(null, new object[] { _stateDown }) as IList; }
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
                }
                else if (_asyncKcType != null && val.GetType() == _asyncKcType && _asyncKcLabel != null)
                {
                    object label = _asyncKcLabel.GetValue(val);
                    if (label == null) continue;
                    ushort labelVal = (ushort)System.Convert.ToInt32(label);

                    // Try the label-based resolution for known labels.
                    if (labelVal != 119 /* KeyLabel.Unknown */ && _asyncToUnity != null)
                    {
                        var resolved = _asyncToUnity.Invoke(null, new object[] { label });
                        if (resolved != null)
                        {
                            int kc = System.Convert.ToInt32(resolved);
                            if (kc != (int)KeyCode.None) resolvedKey = (KeyCode)kc;
                        }
                        allowed = _allowedLabels.Contains(labelVal);
                    }

                    // HID raw-key fallback (native bundle reports modifiers as label=Unknown).
                    if (resolvedKey == KeyCode.None && _asyncKcKey != null)
                    {
                        ushort raw = (ushort)System.Convert.ToInt32(_asyncKcKey.GetValue(val));
                        if (_hidToKeyCode.TryGetValue(raw, out KeyCode mapped))
                        {
                            resolvedKey = mapped;
                            if (!allowed) allowed = _allowed.Contains(mapped);
                        }
                    }
                }

                // Ghost filter — always applies. Ghost-key presses are never input to the game.
                if (resolvedKey != KeyCode.None && _ghosts.Contains(resolvedKey)) continue;

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

        // ── RDInput.GetMain — platform-agnostic aggregator ───────────────────
        [HarmonyPatch]
        private static class GetMainPatch
        {
            static MethodBase TargetMethod()
            {
                var t = AccessTools.TypeByName("RDInput");
                return t != null ? AccessTools.Method(t, "GetMain") : null;
            }

            public static void Postfix(int __0, ref int __result)
            {
                // While the Bismuth menu is open, no gameplay inputs register. Acts on the
                // main port only (__0==0); leaves multiplayer slots untouched.
                if (_blockWhileOpen && __0 == 0 && UICore.IsOpen) { __result = 0; return; }
                // Skip when re-entering (GetStateKeys calls GetMain internally)
                if ((!_active && !_chatterActive && _ghosts.Count == 0) || __result == 0 || __0 != 0 || _inCount) return;
                __result = Mathf.Min(__result, CountAllowedInPressedKeys());
            }
        }

        // ── Fallback: block accuracy recording for non-allowed key presses ────
        [HarmonyPatch(typeof(scrMarginTracker), "AddHit", new[] { typeof(HitMargin) })]
        private static class AddHitBlockPatch
        {
            public static bool Prefix()
            {
                // While the menu is open, no hits register — same gate as the GetMain block.
                if (_blockWhileOpen && UICore.IsOpen) return false;
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
