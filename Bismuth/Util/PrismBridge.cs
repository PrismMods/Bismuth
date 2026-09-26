using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PrismLib.UI.Toolkit;
using UnityEngine;

namespace Bismuth
{
    /* Bismuth's side of PrismLib (github.com/PrismMods/PrismLib).

       Two things here were costing us silently. Bismuth blocks the keyboard game-wide while its
       panel is open, which kills every Sapphire hotkey for as long as the panel is up; and Sapphire
       hides the gameplay HUD in Editor Mode, which it used to tell us through a reflection poke at
       Settings.ExternalEditorSuppress. Both are now claims, so either mod can see the other.

       EVERY PrismLib type stays inside this file, in METHOD BODIES only, and every method here that
       touches one is called solely after Available has been checked. Two separate rules, both
       load-bearing on a machine where the library never arrived — which is exactly the machine that
       must keep working:

         - No PrismLib type may appear in a FIELD, because field types are part of the class layout
           and are resolved when the type itself loads, before any method runs. A `ModHandle _me`
           field made this whole class unloadable and took the mod down with it:
           "TypeLoadException - Could not load type of field 'Bismuth.PrismBridge:_me'". Hence the
           object fields and the casts.
         - No caller outside this file may mention a PrismLib type, because the CLR resolves a
           method's types when that method is JITted. */
    internal static class PrismBridge
    {
        internal static bool Available { get; private set; }

        internal static void Init(string version)
        {
            try
            {
                if (!PrismLib.Bootstrap.PrismBootstrap.Ensure(BismuthLog.Log, new Version(0, 2, 0))) return;
                Wire(version);
                Available = true;
            }
            catch (Exception e) { BismuthLog.Log("PrismLib unavailable: " + e.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Wire(string version)
        {
            _version = "v" + (version ?? "?");
            Version v;
            Version.TryParse((version ?? "0.0.0").Split('-')[0], out v);
            var me = PrismLib.Prism.Register("Bismuth", v ?? new Version(0, 0, 0));
            _me = me;
            PrismLib.Prism.Log = BismuthLog.Log;
            PrismLib.Keys.KeyName = i => ((KeyCode)i).ToString();
            // So the other mods' scroll-zoom and click handling can stand down over our panels.
            me.ReportPointerOverUi(() => UI.UICore.IsOpen);
            me.BindKey("SettingsPanel", (int)KeyCode.B, PrismLib.KeyMods.Ctrl, "Open the settings panel");
            me.BindKey("DebugPanel", (int)KeyCode.D, PrismLib.KeyMods.Alt, "Open the debug panel");
            me.BindKey("SettingsPanelPrism", (int)KeyCode.S, PrismLib.KeyMods.Alt, "Open the Prism settings panel");

            // What this mod contributes to the shared debug window. Pulled, never pushed: the
            // delegates only run while someone is looking at that tab.
            TakeDebugClaim(me);
            me.AddLog("log", () => BismuthLog.ReadTail(200000).Split('\n'), BismuthLog.LogPath, BismuthLog.Clear);
            me.AddFields("Player", PlayerFields);
        }

        // Opaque on purpose — see the class comment. Cast at use, never in the field type.
        private static string _version = "";
        private static object _me;
        private static object _input;

        /* Held while KeyLimiter is swallowing keys game-wide. Driven from OnUpdate rather than from
           the BlockInputs property, which is read several times a frame from Harmony postfixes. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SyncInputClaim(bool blocking)
        {
            try
            {
                var me = _me as PrismLib.ModHandle;
                if (blocking) { if (_input == null && me != null) _input = me.ClaimState(PrismLib.StateKey.InputCapture, "panel open"); }
                else if (_input != null) { ((PrismLib.Claim)_input).Release(); _input = null; }
            }
            catch { }
        }

        /* The shared font store: <mods root>/PrismLib/Fonts. Packs installed there are visible
           to every Prism mod, so a 30 MB Korean family is downloaded once rather than per mod.
           Returns null with PrismLib absent, and FontLoader simply scans one directory fewer. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string FontDir()
        {
            try { return PrismLib.Fonts.Dir; } catch { return null; }
        }

        // Cached per frame: the HUD readers below are properties consulted many times a frame.
        private static int _hudFrame = -1;
        private static bool _hudHeld;

        /// True while another mod owns the gameplay HUD (Sapphire's Editor Mode does), so Bismuth's
        /// overlays stand down instead of drawing over it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool HudHeldElsewhere()
        {
            try
            {
                if (Time.frameCount == _hudFrame) return _hudHeld;
                _hudFrame = Time.frameCount;
                var owner = PrismLib.Claims.OwnerOf(PrismLib.StateKey.GameHud);
                return _hudHeld = owner != null && owner != "Bismuth";
            }
            catch { return false; }
        }

        // ── shared debug window ──────────────────────────────────────────────

        /* Ctrl+Shift+D, the SAME chord in every Prism mod, because the window itself is shared: it
           shows every registered mod's log and fields, not just this one's. Both mods poll the
           chord; the StateKey.DebugPanel claim decides which one actually draws, so the user gets
           one window instead of two identical ones.

           A typed PrismLib.UI field is fine here. That assembly ships inside the mod folder, unlike
           PrismLib.dll, which may never install — which is why the handles above stay object.

           No typing guard: while Bismuth's own panel is open its KeyLimiter block swallows every
           key but B, so this chord cannot fire from a text field anyway. */
        private static DebugPanel _debug;
        private static SettingsPanel _settings;
        private static object _debugClaim;
        // Plain bool, so TickDebug can consult it without touching a PrismLib type.
        private static bool _ownsDebug;

        internal static void TickDebug()
        {
            /* Alt+Shift+D and Alt+Shift+S.

               Plain Alt+letter does not arrive on macOS: Option+D and Option+S compose a character
               (∂, ß) and Unity never reports the letter's keycode. Adding Shift stops the
               composition, and Alt+Shift+S was verified working in-game while Alt+S was not — which
               is also why the first diagnostic saw LeftAlt and the letter together but the chord
               never fired. Ctrl+Shift+D and Ctrl+Shift+P stay as fallbacks; Ctrl+Shift+S is the
               editor's Save and is deliberately not one of them. */
            bool alt = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                     || Input.GetKey(KeyCode.AltGr))
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            bool fallback = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                          || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand))
                         && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            AltDiag(alt);
            TickCapture();
            TickStore();
            // The wheel has to be polled: this game's input module does not forward it to UI
            // Toolkit panels, so a log would otherwise be unscrollable.
            PrismLib.UI.Toolkit.Ui.TickWheel(Input.mousePosition, Input.mouseScrollDelta.y);

            /* Undo and redo, only while a Prism window is open so the editor keeps its own Ctrl+Z
               the rest of the time. GetKeyDown on Z is fine here: the chord needs no Alt, so none
               of the macOS composition that broke Alt+letter applies. */
            if (PrismLib.UI.Toolkit.Ui.AnyWindowOpen && Input.GetKeyDown(KeyCode.Z)
                && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                 || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    PrismLib.UI.Toolkit.History.Redo();
                else PrismLib.UI.Toolkit.History.Undo();
            }

            /* Escape closes the top Prism window. Only consumed when one was actually open, so the
               editor keeps its own Escape the rest of the time. */
            if (Input.GetKeyDown(KeyCode.Escape) && PrismLib.UI.Toolkit.Ui.AnyWindowOpen)
                PrismLib.UI.Toolkit.Ui.CloseTop();
            if (Available && !_ownsDebug) { if (_debug != null) _debug.Tick(); return; }

            /* Build it before it is wanted. Creating the panel costs a UIDocument, the first OS
               font atlas and the list's first layout — perhaps a frame and a half, which is a
               visible hitch if it lands on the keypress. Paying it once a few seconds into the
               session, while nobody is waiting, makes the first open instant. */
            if (_debug == null && Time.frameCount > 300) BuildDebug();
            // The settings window was still built on its keypress, which is the hitch coming back
            // by another door. Staggered so the two builds never land on the same frame.
            else if (_settings == null && Available && Time.frameCount > 420) BuildSettings();
            // Alt+S: every Prism mod's settings, drawn from the schema they describe.
            if ((alt && Input.GetKeyDown(KeyCode.S)) || (fallback && Input.GetKeyDown(KeyCode.P))) ToggleSettings();
            if (_settings != null) _settings.Tick();
            if (_menu != null) _menu.Tick();
            // A capture in progress owns the keyboard: do not also open a window with it.
            if (_capture != null) return;
            if ((alt && Input.GetKeyDown(KeyCode.D)) || (fallback && Input.GetKeyDown(KeyCode.D))) ToggleDebug();
            if (_debug != null) _debug.Tick();
        }

        internal static void ToggleDebug()
        {
            /* Everything here is logged, because the game disables exception capturing: without
               these lines a broken panel and a hotkey that never fired look identical. */
            try
            {
                if (Available && !_ownsDebug)
                {
                    // Another mod owns the window. Our button still works; it drives theirs.
                    RequestToggle();
                    return;
                }
                if (_debug == null) BuildDebug();
                if (_debug != null) _debug.Toggle();
            }
            catch (Exception e) { BismuthLog.Log("[prism] debug window FAILED: " + e); }
        }

        /* One line, once per Alt press, naming every key the runtime reports while Alt is held.
           The game disables exception capturing and Unity's macOS key handling is the suspect, so
           guessing at it from here is hopeless — this says what actually arrives. */
        private static Action<string> _capture;
        private static bool _altWas;

        /* Keybind capture: the next key pressed wins, Escape cancels. Polled here rather than read
           from a UI Toolkit event because Tab, the arrows and Escape are all worth binding and all
           get eaten as navigation inside a panel. */
        private static void TickCapture()
        {
            if (_capture == null) return;
            if (!Input.anyKeyDown) return;
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if (!Input.GetKeyDown(k)) continue;
                if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;
                var done = _capture;
                _capture = null;
                done(k == KeyCode.Escape ? null : k.ToString());
                return;
            }
        }
        private static void AltDiag(bool alt)
        {
            // What goes DOWN while Alt is held is the actual question — the rising edge only
            // showed keys that were already pressed.
            if (alt && Input.anyKeyDown)
            {
                var seen = "";
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                    if (Input.GetKeyDown(k) && (k < KeyCode.Mouse0 || k > KeyCode.Mouse6))
                        seen += (seen.Length > 0 ? " " : "") + k;
                if (seen.Length > 0) BismuthLog.Log("[prism] alt+" + seen);
            }
            _altWas = alt;
        }

        /* Window geometry outlives the session. A flat key=value file beside the mod's own data,
           not XML through the settings class: this is written on every drag frame, and it must not
           be able to corrupt real settings if the game dies mid-write. */
        private static string _storePath;
        private static Dictionary<string, string> _store;

        private static void WireWindowStore()
        {
            if (_store != null) return;
            _store = new Dictionary<string, string>();
            try
            {
                _storePath = System.IO.Path.Combine(MainClass.ModPath, "PrismWindows.txt");
                if (System.IO.File.Exists(_storePath))
                    foreach (var line in System.IO.File.ReadAllLines(_storePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) _store[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
            }
            catch (Exception e) { BismuthLog.Log("[prism] window store unreadable: " + e.Message); }

            PrismLib.UI.Toolkit.Window.Read = k =>
            {
                string v;
                return _store.TryGetValue(k, out v) ? v : null;
            };
            PrismLib.UI.Toolkit.Window.Write = (k, v) =>
            {
                string had;
                if (_store.TryGetValue(k, out had) && had == v) return;   // dragging writes every frame
                _store[k] = v;
                _storeDirty = true;
            };
        }

        private static bool _storeDirty;
        private static float _storeFlushAt;

        /// Flush at most once a second: a drag would otherwise write the file on every frame.
        private static void TickStore()
        {
            if (!_storeDirty || _store == null || _storePath == null) return;
            if (Time.unscaledTime < _storeFlushAt) return;
            _storeFlushAt = Time.unscaledTime + 1f;
            _storeDirty = false;
            try
            {
                var lines = new List<string>();
                foreach (var kv in _store) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(_storePath, lines.ToArray());
            }
            catch (Exception e) { BismuthLog.Log("[prism] window store unwritable: " + e.Message); }
        }

        /* Bismuth's OWN menu, separate from the Prism settings window above. Alt+Shift+S is the
           library's; this is the mod's, and it is what Ctrl+B will open once every page has moved
           across. Its own panel instance, so the two do not fight over one window. */
        private static SettingsPanel _menu;

        internal static void ToggleMenu()
        {
            try
            {
                if (_menu == null)
                {
                    WireWindowStore();
                    UI.Theme.PushTokens();
                    PrismLib.UI.Toolkit.Ui.Log = m => BismuthLog.Log("[prism] " + m);
                    if (Available) UseSharedScorer();
                    _menu = new SettingsPanel("Bismuth", () => UI.Prism.PrismMenu.Build());
                    _menu.SetFooter("Ctrl + B to open or close", _version);
                }
                _menu.Toggle();
            }
            catch (Exception e) { BismuthLog.Log("[prism] Bismuth menu FAILED: " + e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UseSharedScorer()
            => PrismLib.UI.Toolkit.SearchIndex.Scorer = (text, q) => PrismLib.Search.Score(text, q);

        internal static void ToggleSettings()
        {
            try
            {
                if (!Available) { BismuthLog.Log("[prism] settings window needs PrismLib"); return; }
                if (_settings == null) BuildSettings();
                if (_settings != null) _settings.Toggle();
            }
            catch (Exception e) { BismuthLog.Log("[prism] settings window FAILED: " + e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BuildSettings()
        {
            try
            {
                UI.Theme.PushTokens();
                WireWindowStore();
                PrismLib.UI.Toolkit.Ui.Log = m => BismuthLog.Log("[prism] " + m);
                _settings = new SettingsPanel("Prism · Settings", SettingRows);
                _settings.Matcher = (r, q) => PrismLib.Search.ScoreAny(q, r.Label, r.Group, r.Page, r.Owner)
                                              != PrismLib.Search.NoMatch;
                // Same as the debug window: the OS font reads better than the game's display face.
            }
            catch (Exception e) { BismuthLog.Log("[prism] settings window build FAILED: " + e); }
        }

        /* PrismLib.SettingEntry -> the panel's own row type. The conversion is the price of
           PrismLib.UI not referencing PrismLib.dll, and it is worth paying: the UI half ships with
           the mod and has to work when the other one never installed. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerable<SettingRow> SettingRows()
        {
            var outp = new List<SettingRow>();
            foreach (var kv in PrismLib.Settings.Search(""))
            {
                var e = kv.Value;
                outp.Add(new SettingRow
                {
                    Owner = kv.Key, Page = e.Page, Group = e.Group, Label = e.Label, Tooltip = e.Tooltip,
                    Kind = Kind(e.Kind), Get = e.Get, Set = e.Set,
                    Capture = e.Kind == PrismLib.SettingKind.Key ? Capturer(e) : null,
                    Min = (float)e.Min, Max = (float)e.Max,
                    Options = e.Options, ActionLabel = e.ActionLabel,
                });
            }
            return outp;
        }

        /* Hands the panel a way to start a capture without either side knowing how the other
           works: the panel calls this, we poll raw input, the mod's own setter takes the result,
           and the callback tells the row what to display. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Action<Action<string>> Capturer(PrismLib.SettingEntry e)
            => done => _capture = name =>
            {
                if (name != null && e.Set != null) e.Set(name);
                done(e.Get != null ? Convert.ToString(e.Get()) : name);
            };

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static SettingControl Kind(PrismLib.SettingKind k)
        {
            switch (k)
            {
                case PrismLib.SettingKind.Int:    return SettingControl.Int;
                case PrismLib.SettingKind.Float:  return SettingControl.Float;
                case PrismLib.SettingKind.Enum:   return SettingControl.Choice;
                case PrismLib.SettingKind.Text:   return SettingControl.Text;
                case PrismLib.SettingKind.Action: return SettingControl.Action;
                case PrismLib.SettingKind.Colour: return SettingControl.Colour;
                case PrismLib.SettingKind.Key:    return SettingControl.Key;
                default:                          return SettingControl.Bool;
            }
        }

        private static IEnumerable<DebugTab> DebugTabs()
        {
            var tabs = new List<DebugTab>();
            if (Available) AddPrismTabs(tabs);
            // Standalone: nothing registered anywhere because PrismLib never installed. Show ours.
            if (tabs.Count == 0)
                tabs.Add(new DebugTab { Name = "Bismuth · log", Lines = () => BismuthLog.ReadTail().Split('\n') });
            return tabs;
        }

        private static void BuildDebug()
        {
            try
            {
                if (_debug != null) return;
                WireWindowStore();
                UI.Theme.PushTokens();   // shared window wears this mod's palette
                PrismLib.UI.Toolkit.Ui.Log = m => BismuthLog.Log("[prism] " + m);
                _debug = new DebugPanel("Prism · Debug", DebugTabs);
                if (Available) UseSharedMatcher(_debug);
                /* No SetFont: a mod's TMP asset is built from the GAME's display font, which is
                   the wrong face for a wall of log text and was unreadable at size. Surface's own
                   OS font is the readable default, and the log list uses a monospace. */
            }
            catch (Exception e) { BismuthLog.Log("[prism] debug window build FAILED: " + e); }
        }


        /* Ownership is settled ONCE, at registration, not on the first keypress.

           Both mods used to poll the chord and the loser would ask the winner to toggle — in the
           same frame the winner had already toggled it, so the window opened and shut again and the
           hotkey looked dead while the settings button worked. Now only the owner watches the
           keyboard; everyone else routes through RequestToggle, which is a single call. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TakeDebugClaim(PrismLib.ModHandle me)
        {
            try
            {
                if (me == null) return;
                _debugClaim = me.ClaimState(PrismLib.StateKey.DebugPanel, "shared debug window");
                if (_debugClaim == null) return;
                _ownsDebug = true;
                PrismLib.Diagnostics.ToggleRequested += ToggleDebug;   // answer everyone else
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RequestToggle() => PrismLib.Diagnostics.RequestToggle();

        /* The filter uses PrismLib's matcher when it is there, so typing "efbpm" finds
           "Effective BPM" in the debug window exactly as it does in a settings search. Injected
           rather than referenced, because PrismLib.UI must not depend on PrismLib.dll — the UI half
           ships with the mod and has to work when the other one never installed. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UseSharedMatcher(DebugPanel p)
            => p.Matcher = (line, query) => PrismLib.Search.Matches(line, query);



        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddPrismTabs(List<DebugTab> tabs)
        {
            foreach (var log in PrismLib.Diagnostics.Logs)
            {
                var src = log;
                tabs.Add(new DebugTab { Name = src.Owner + " · " + src.Name, Lines = src.Tail });
            }
            foreach (var group in PrismLib.Diagnostics.Fields)
            {
                var g = group;
                tabs.Add(new DebugTab { Name = g.Owner + " · " + g.Name, Lines = () => Pairs(g.Read()) });
            }
            tabs.Add(new DebugTab { Name = "Prism", Lines = PrismState });
        }

        private static IEnumerable<string> Pairs(IEnumerable<KeyValuePair<string, string>> src)
        {
            foreach (var kv in src) yield return kv.Key + " = " + kv.Value;
        }

        /// Who is loaded, who owns what, and which hotkeys collide — the answer to "autoplay is
        /// broken" that used to take three log files to guess at.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerable<string> PrismState()
        {
            var outp = new List<string>();
            try
            {
                outp.Add("PrismLib " + PrismLib.Prism.Version);
                foreach (var id in PrismLib.Prism.LoadedMods)
                {
                    Version v;
                    PrismLib.Prism.IsLoaded(id, out v);
                    outp.Add("  mod  " + id + " " + v);
                }
                outp.Add("");
                outp.Add("— state claims —");
                foreach (var kv in PrismLib.Claims.Held) outp.Add("  " + kv.Key + "  ->  " + kv.Value);
                outp.Add("");
                outp.Add("— key conflicts —");
                bool any = false;
                foreach (var c in PrismLib.Keys.Conflicts()) { outp.Add("  " + c.Key + "  vs  " + c.Value); any = true; }
                if (!any) outp.Add("  (none)");
                outp.Add("");
                outp.Add("— registered keys —");
                foreach (var k in PrismLib.Keys.All) outp.Add("  " + k);
            }
            catch (Exception e) { outp.Add("(unavailable: " + e.Message + ")"); }
            return outp;
        }

        private static IEnumerable<KeyValuePair<string, string>> PlayerFields()
        {
            var outp = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, v) => outp.Add(new KeyValuePair<string, string>(k, v));
            try
            {
                add("Bismuth", MainClass.IsEnabled ? "enabled" : "disabled");
                add("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                var sc = scrController.instance;
                add("controller", sc != null ? sc.state.ToString() : "none");
                if (sc != null) add("no-fail", sc.noFail ? "on" : "off");
                add("autoplay", RDC.auto ? "on" : "off");
                add("hud hidden", RDC.noHud ? "yes" : "no");
                add("key block", KeyLimiter.BlockingInputs ? "ACTIVE (other mods' hotkeys are dead)" : "off");
            }
            catch (Exception e) { add("(read failed)", e.Message); }
            return outp;
        }

        /* Callable with PrismLib absent, so it mentions no PrismLib type: the debug window is a
           PrismLib.UI object that exists either way, and everything else is behind Available in a
           method that is only ever JITted when the library is there. */
        internal static void Shutdown()
        {
            if (_debug != null) { _debug.Dispose(); _debug = null; }
            if (_settings != null) { _settings.Dispose(); _settings = null; }
            if (_menu != null) { _menu.Dispose(); _menu = null; }
            if (!Available) return;
            Available = false;
            ReleasePrism();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReleasePrism()
        {
            try
            {
                _input = null; _debugClaim = null; _ownsDebug = false;
                PrismLib.Claims.ReleaseAll("Bismuth");
                PrismLib.Keys.Unregister("Bismuth");
                PrismLib.Diagnostics.ToggleRequested -= ToggleDebug;
                PrismLib.Diagnostics.Unregister("Bismuth");
            }
            catch { }
        }
    }
}
