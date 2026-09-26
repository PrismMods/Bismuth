using System;
using System.Collections.Generic;
using PrismLib.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bismuth.UI.Prism
{
    /* Bismuth's menu, rebuilt on the PrismLib framework.

       The rail uses GROUPS, not nested branches: a heading with its pages always visible. Nesting
       hides things, and the problem being solved here is that things are hard to find — Overlay was
       56 controls on one page. Grouping splits it without making anyone expand anything.

       Ported page by page, so this and the old uGUI panel coexist: both edit the same Settings
       object, so nothing is stranded while the move is half done. A page that has not crossed over
       yet simply is not in this rail.

       The position editor is deliberately ONE page for every element, not one per group — moving
       things around is a single task and splitting it by which panel owns each element would make
       it worse. */
    internal static class PrismMenu
    {
        internal static List<NavItem> Build()
        {
            return new List<NavItem>
            {
                NavItem.Heading("Overlay",
                    new NavItem("Readouts", OverlayReadouts),
                    new NavItem("Judgement", OverlayJudgement),
                    new NavItem("Timing", OverlayTiming),
                    new NavItem("Style", OverlayStyle)),
                NavItem.Heading("Interface",
                    new NavItem("Hide UI", HideUi),
                    new NavItem("Appearance", Appearance),
                    new NavItem("Fonts", FontPacksPage)
                    {
                        // Refresh() fetches on a worker; the count changing is how the page knows.
                        Revision = () => FontPacks.Available != null ? FontPacks.Available.Count : 0,
                    },
                    new NavItem("Profiles", ProfilesPage),
                    new NavItem("Game UI", GameUi),
                    new NavItem("Positions", Positions)),
                NavItem.Heading("Gameplay",
                    new NavItem("Input", Input),
                    new NavItem("Tweaks", Tweaks)),
                NavItem.Heading("System",
                    new NavItem("Diagnostics", Diagnostics)),
            };
        }

        private static Settings S => MainClass.Settings;

        /* The same notification the uGUI pages raise, so both views drive the identical apply
           path and the two cannot disagree about live state while they coexist. */
        private static void Apply()
        {
            var notify = UICore.OnSettingsChanged;
            if (notify != null) notify();
        }

        /* Hide UI, the first page across. Judgement hiding is nested under its own switch in the
           old page; here it is a section, which reads the same and costs no interaction. */
        private static void HideUi(VisualElement body)
        {
            /* Each switch hides what it governs. The old page showed every row whatever the master
               switch said, which is how people end up changing a setting the mod is ignoring. */
            var on = Widgets.ToggleGroup(body, "Enable", S.HideUiEnabled,
                                         v => { S.HideUiEnabled = v; Apply(); },
                                         "Master switch for everything on this page");

            Widgets.Toggle(on, "Hide all UI", S.HideAllUI, v => { S.HideAllUI = v; Apply(); },
                           "RDC.noHud — hides the game's entire HUD");

            var elements = Widgets.Section(on, "Elements");
            Widgets.Toggle(elements, "Hit error meter", S.HideHitmeter, v => { S.HideHitmeter = v; Apply(); });
            Widgets.Toggle(elements, "Autoplay text", S.HideAutoplayText, v => { S.HideAutoplayText = v; Apply(); });
            Widgets.Toggle(elements, "Autoplay icon", S.HideAutoplayIcon, v => { S.HideAutoplayIcon = v; Apply(); });
            Widgets.Toggle(elements, "No-fail badge", S.HideNoFail, v => { S.HideNoFail = v; Apply(); });
            Widgets.Toggle(elements, "Difficulty", S.HideDifficulty, v => { S.HideDifficulty = v; Apply(); });
            Widgets.Toggle(elements, "Level name", S.HideLevelName, v => { S.HideLevelName = v; Apply(); });
            Widgets.Toggle(elements, "Controls tip", S.HideControlsTip, v => { S.HideControlsTip = v; Apply(); });
            Widgets.Toggle(elements, "Beta build label", S.HideBetaBuild, v => { S.HideBetaBuild = v; Apply(); });

            var judge = Widgets.Section(on, "Judgements");
            var judging = Widgets.ToggleGroup(judge, "Hide judgements", S.HideJudgementsEnabled,
                                              v => { S.HideJudgementsEnabled = v; Apply(); });
            Widgets.Toggle(judging, "All", S.HideJudgementsAll, v => { S.HideJudgementsAll = v; Apply(); });
            Widgets.Toggle(judging, "Perfect", S.HideJudgementsPerfect, v => { S.HideJudgementsPerfect = v; Apply(); });
            Widgets.Toggle(judging, "Pure perfect", S.HideJudgementsXPerfect, v => { S.HideJudgementsXPerfect = v; Apply(); });
            Widgets.Toggle(judging, "Early / late perfect", S.HideJudgementsELPerfect, v => { S.HideJudgementsELPerfect = v; Apply(); });
            Widgets.Toggle(judging, "Early / late", S.HideJudgementsEarlyLate, v => { S.HideJudgementsEarlyLate = v; Apply(); });
            Widgets.Toggle(judging, "Miss", S.HideJudgementsMiss, v => { S.HideJudgementsMiss = v; Apply(); });
            Widgets.Toggle(judging, "Death", S.HideJudgementsDeath, v => { S.HideJudgementsDeath = v; Apply(); });
        }

        /* Overlay, split the four ways the survey suggested: 63 controls on one page was the worst
           offender in the mod, and a rail item wants five to nine rows.

           What lives here is WHAT is shown. Where each readout sits and how big it is belongs to
           the on-screen position editor, which is a better tool for it than a column of numbers —
           so those rows are not reproduced here. */
        private static void OverlayReadouts(VisualElement body)
        {
            var on = Widgets.ToggleGroup(body, "Enable", S.ShowOverlay,
                                         v => { S.ShowOverlay = v; Apply(); },
                                         "Master switch for every overlay readout");

            var score = Widgets.Section(on, "Score");
            Stat(score, "Progress", "progress", () => S.ShowProgress, v => S.ShowProgress = v);
            Stat(score, "Best progress", "bestprogress", () => S.ShowBestProgress, v => S.ShowBestProgress = v);
            Widgets.Toggle(score, "Progress bar", S.ShowProgressBar, v => { S.ShowProgressBar = v; Apply(); });
            Stat(score, "Accuracy", "acc", () => S.ShowAcc, v => S.ShowAcc = v);
            Stat(score, "XAccuracy", "xacc", () => S.ShowXAcc, v => S.ShowXAcc = v);
            Stat(score, "XScore", "xscore", () => S.ShowXScore, v => S.ShowXScore = v);

            var tempo = Widgets.Section(on, "Tempo");
            Stat(tempo, "BPM", "bpm", () => S.ShowBpm, v => S.ShowBpm = v);
            Stat(tempo, "Tile BPM", "tilebpm", () => S.ShowTileBpm, v => S.ShowTileBpm = v);
            Stat(tempo, "Keys per second", "kps", () => S.ShowKps, v => S.ShowKps = v);

            var session = Widgets.Section(on, "Session");
            Widgets.Toggle(session, "Attempts", S.ShowAttempts, v => { S.ShowAttempts = v; Apply(); });
            Widgets.Toggle(session, "Full attempts only", S.ShowFullAttempts, v => { S.ShowFullAttempts = v; Apply(); },
                           "Count only attempts that started from the beginning");
            Widgets.Toggle(session, "Song duration", S.ShowSongDuration, v => { S.ShowSongDuration = v; Apply(); });
            Widgets.Toggle(session, "Level duration", S.ShowLevelDuration, v => { S.ShowLevelDuration = v; Apply(); });
            Widgets.Toggle(session, "FPS", S.ShowFps, v => { S.ShowFps = v; Apply(); });
        }

        /* One readout: shown or not, plus a page of its own for the rest.

           The original opened a subpage per stat, and that shape is right — a readout's colours and
           label are ITS settings, not the overlay page's. Everything each stat has in common lives
           here rather than in twelve copies. */
        private static void Stat(VisualElement parent, string label, string key,
                                 Func<bool> get, Action<bool> set)
        {
            // One row: the switch shows it, the arrow opens everything else about it.
            Widgets.ToggleSubPage(parent, label, get(), v => { set(v); Apply(); }, body =>
            {
                Ui.Muted("Position and size are set by dragging, under Interface \u203a Positions.", body);
                StatColour(body, "Label colour", key, true);
                StatColour(body, "Value colour", key, false);
            }, "Colours and label for " + label);
        }

        private static void StatColour(VisualElement body, string label, string key, bool isLabel)
        {
            var entry = S.StatColorFor(key);
            var kv = isLabel ? entry?.Label : entry?.Value;
            var current = kv != null ? kv.ToColor() : Color.white;
            Widgets.Colour(body, label, current, c =>
            {
                // create: true — a stat has no entry until something sets one of its colours.
                var e = S.StatColorFor(key, create: true);
                var v = new KvColor { R = c.r, G = c.g, B = c.b, A = c.a };
                if (isLabel) e.Label = v; else e.Value = v;
                Apply();
            }, null, true);
        }

        private static void OverlayJudgement(VisualElement body)
        {
            Widgets.Toggle(body, "Judgements", S.ShowJudgements, v => { S.ShowJudgements = v; Apply(); },
                           "The Perfect / Early / Late counters");
            Widgets.Toggle(body, "Hand viewer", S.ShowHandViewer, v => { S.ShowHandViewer = v; Apply(); });
            Widgets.Toggle(body, "Foot viewer", S.ShowFootViewer, v => { S.ShowFootViewer = v; Apply(); });
        }

        private static void OverlayTiming(VisualElement body)
        {
            Widgets.Toggle(body, "Hit error", S.ShowHitError, v => { S.ShowHitError = v; Apply(); });
            Widgets.Toggle(body, "Timing scale", S.ShowTimingScale, v => { S.ShowTimingScale = v; Apply(); });
            Widgets.ToggleGroup(body, "Timing graph", S.ShowTimingGraph,
                                v => { S.ShowTimingGraph = v; Apply(); },
                                "A histogram of how early or late each hit was");
        }

        private static void OverlayStyle(VisualElement body)
        {
            var shadow = Widgets.ToggleGroup(body, "Text shadow", S.OverlayShadowEnabled,
                                             v => { S.OverlayShadowEnabled = v; Apply(); });
            if (S.OverlayShadowColor == null)
                S.OverlayShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
            var sc = S.OverlayShadowColor;
            Widgets.Colour(shadow, "Shadow colour", new Color(sc.R, sc.G, sc.B, sc.A),
                           c => { sc.R = c.r; sc.G = c.g; sc.B = c.b; sc.A = c.a; Apply(); },
                           null, true);

            Widgets.Toggle(body, "Apply the master font to all overlays", S.OverlayMasterFontEnabled,
                           v =>
                           {
                               S.OverlayMasterFontEnabled = v;
                               MainClass.ApplySelectedFont();
                               Apply();
                           },
                           "Off: each overlay part keeps its own font");

            Ui.Muted("Position and size are set by dragging, under Interface \u203a Positions.", body);
        }

        /* Appearance. The accent picker is the real one now, so the swatch presets the old page
           carried are gone: picking any colour is strictly more than picking one of eight. */
        private static void Appearance(VisualElement body)
        {
            /* A language change rebuilds every panel, so it force-reloads exactly as the old page
               did rather than leaving half the UI in the previous language. */
            Widgets.Choice(body, "Language", new[] { "Follow game", "English", "한국어" },
                           Mathf.Clamp(S.PanelLanguage, 0, 2), i =>
                           {
                               S.PanelLanguage = i;
                               Apply();
                               MainClass.RequestForceReload();
                           });

            Widgets.Slider(body, "UI scale", S.UiScale, 0.6f, 1.6f,
                           v => { S.UiScale = v; Apply(); }, "Size of Bismuth's own panels");
            Widgets.Toggle(body, "Include system fonts", S.IncludeSystemFonts,
                           v => { S.IncludeSystemFonts = v; Apply(); },
                           "Offer every font installed on this machine, not just the bundled ones");

            var accent = Widgets.Section(body, "Accent");

            /* The colour only appears once a custom one is asked for — showing a picker that the
               mod is ignoring is how the old page confused people. Held in its own container so
               the toggle can hide it without rebuilding the page. */
            var custom = Widgets.ToggleGroup(accent, "Use a custom colour", S.UiAccentCustom,
                                             v => { S.UiAccentCustom = v; Apply(); });
            Widgets.Colour(custom, "Accent colour",
                           new Color(S.UiAccentR, S.UiAccentG, S.UiAccentB, 1f),
                           c => { UICore.ApplyAccent(c); Apply(); });

            Widgets.Toggle(accent, "Apply accent as theme colour", S.AccentAsTheme,
                           v => { S.AccentAsTheme = v; Apply(); },
                           "Tints the panels themselves, not just the highlights");
        }

        /* Font packs: downloadable sets, so each entry is installed or not rather than on or off.
           The list is rebuilt rather than updated, because Install and Remove both change what the
           row should offer and a half-updated row is worse than a redrawn one. */
        private static void FontPacksPage(VisualElement body)
        {
            Ui.Muted("Font packs add families Bismuth can use for its panels and overlays.", body);

            var list = Ui.Box(body);
            Action rebuild = null;
            rebuild = () =>
            {
                list.Clear();
                var packs = FontPacks.Available;
                if (packs == null || packs.Count == 0)
                {
                    Ui.Muted("No packs listed. Refresh to fetch the list.", list);
                    return;
                }
                foreach (var pack in packs)
                {
                    var p = pack;
                    bool installed = FontPacks.IsInstalled(p.Id);
                    var actions = Widgets.Item(list, p.Name, installed ? "Installed" : null);
                    if (installed)
                    {
                        Ui.Btn("Open folder", () => OsShell.OpenFolder(FontPacks.InstallDir(p.Id)), actions);
                        Ui.Btn("Remove", () =>
                        {
                            string err;
                            if (!FontPacks.Remove(p.Id, out err)) BismuthLog.Log("Font pack remove failed: " + err);
                            rebuild();
                        }, actions);
                    }
                    else Ui.Btn("Install", () => { FontPacks.Install(p); rebuild(); }, actions);
                }
            };
            rebuild();

            var row = Ui.Row(body);
            Ui.Btn("Refresh list", () => { FontPacks.Refresh(); rebuild(); }, row);
        }

        /* Profiles: a saved copy of every setting. Built-in ones cannot be deleted, which the list
           shows by simply not offering the button rather than by refusing the click. */
        private static void ProfilesPage(VisualElement body)
        {
            var list = Ui.Box(body);
            string pending = "";
            Action rebuild = null;
            rebuild = () =>
            {
                list.Clear();
                var names = new List<string>(Profiles.BuiltIn);
                names.AddRange(Profiles.ListSaved());
                foreach (var name in names)
                {
                    var n = name;
                    bool active = S.ActiveProfile == n;
                    var actions = Widgets.Item(list, n, Profiles.IsBuiltIn(n) ? "Built in" : null);
                    if (active) Widgets.MarkActive(actions);
                    Ui.Btn("Load", () =>
                    {
                        string err;
                        if (!Profiles.Load(n, out err)) BismuthLog.Log("Profile load failed: " + err);
                        Apply();
                        rebuild();
                    }, actions);
                    if (!Profiles.IsBuiltIn(n))
                        Ui.Btn("Delete", () =>
                        {
                            string err;
                            if (!Profiles.Delete(n, out err)) BismuthLog.Log("Profile delete failed: " + err);
                            rebuild();
                        }, actions);
                }
            };
            rebuild();

            Widgets.Text(body, "New profile name", "", v => pending = v,
                         "Saves every current setting under this name");
            var row = Ui.Row(body);
            Ui.Btn("Save current settings", () =>
            {
                string err;
                if (!Profiles.SaveCurrent(pending, out err)) BismuthLog.Log("Profile save failed: " + err);
                rebuild();
            }, row);
            Ui.Btn("Open folder", () => OsShell.OpenFolder(Profiles.ProfilesDir()), row);
        }

        /* The game's own text and meters — size and font, not position. Where each piece SITS is
           the on-screen editor's job, same as the overlay's. */
        private static void GameUi(VisualElement body)
        {
            var text = Widgets.Section(body, "Text");
            Widgets.Slider(text, "Game text size", S.GameTextScale, 0.5f, 2f,
                           v => { S.GameTextScale = v; Apply(); });
            Widgets.Slider(text, "Line spacing", S.GameTextLineSpacing, 0.5f, 2f,
                           v => { S.GameTextLineSpacing = v; Apply(); });
            Widgets.Slider(text, "Level stats size", S.GameStatsScale, 0.5f, 2f,
                           v => { S.GameStatsScale = v; Apply(); });
            Widgets.Slider(text, "Judgement size", S.GameJudgementScale, 0.5f, 2f,
                           v => { S.GameJudgementScale = v; Apply(); });

            var font = Widgets.Section(body, "Font");
            Widgets.Toggle(font, "Use the overlay's font", S.GameTextUseOverlayFont,
                           v => { S.GameTextUseOverlayFont = v; MainClass.ApplySelectedFont(); Apply(); },
                           "Off: the game's own font is left alone");

            var meter = Widgets.Section(body, "Error meter");
            var over = Widgets.ToggleGroup(meter, "Override the game's meter", S.GameErrorMeterOverride,
                                           v => { S.GameErrorMeterOverride = v; Apply(); });
            Widgets.Slider(over, "Scale", S.GameErrorMeterScale, 0.25f, 3f,
                           v => { S.GameErrorMeterScale = v; Apply(); });

            Ui.Muted("Positions are set by dragging, under Interface \u203a Positions.", body);
        }

        /* ONE page for every element's position, deliberately. Moving things around is a single
           task; splitting it by which panel owns each element would make it harder, not easier.
           The on-screen editors stay as they are — dragging in place beats typing coordinates. */
        private static void Positions(VisualElement body)
        {
            Ui.Muted("Drag elements where you want them, on the real screen.", body);
            // One editor, not two: LocationEditor.Open is GameUiEditor in overlay mode, and the
            // two layers are switched inside it rather than entered separately.
            Widgets.Action(body, "Edit positions", "Open editor", () => GameUiEditor.Open(),
                           "Bismuth's overlays and the game's own HUD, in one editor");
            Widgets.DangerButton(body, "Reset all positions",
                                 () => { GameUiLayout.ResetAllToDefaults(); Apply(); },
                                 "Puts every element back where the game or Bismuth had it");
        }

        private static void Input(VisualElement body)
        {
            Widgets.Toggle(body, "Block game input while the menu is open", S.BlockInputsWhileMenuOpen,
                           v => { S.BlockInputsWhileMenuOpen = v; Apply(); },
                           "Keys typed at Bismuth do not reach the game. Prism windows are exempt.");

            var limiter = Widgets.Section(body, "Key limiter");
            Widgets.Toggle(limiter, "Enable", S.KeyLimiterEnabled, v => { S.KeyLimiterEnabled = v; Apply(); });
            Widgets.Toggle(limiter, "Use the key viewer's keys", S.KeyLimiterUseKvKeys,
                           v => { S.KeyLimiterUseKvKeys = v; Apply(); },
                           "Takes the allowed list from the active key viewer preset");

            var chatter = Widgets.Section(body, "Chatter blocker");
            Widgets.Toggle(chatter, "Enable", S.ChatterBlockerEnabled,
                           v => { S.ChatterBlockerEnabled = v; Apply(); });
            Widgets.IntSlider(chatter, "Threshold (ms)", S.ChatterThresholdMs, 0, 60,
                              v => { S.ChatterThresholdMs = v; Apply(); },
                              "Repeats of one key closer together than this are dropped");
        }

        private static void Tweaks(VisualElement body)
        {
            // Stored 0..1, shown as a percentage: the old label said "%" beside a 0.70, leaving
            // the conversion to the reader.
            Widgets.Slider(body, "CLS preview volume", S.ClsPreviewVolume * 100f, 0f, 100f,
                           v => { S.ClsPreviewVolume = v / 100f; Apply(); },
                           "Volume of the custom level select's song preview", "0");

            var gameplay = Widgets.Section(body, "Gameplay");
            Widgets.Toggle(gameplay, "Hide planet orbit rings", S.HidePlanetRings,
                           v => { S.HidePlanetRings = v; Apply(); },
                           "Hides the dotted circle drawn around each planet");
        }

        private static void Diagnostics(VisualElement body)
        {
            Widgets.Action(body, "Log and mod state", "Open", PrismBridge.ToggleDebug,
                           "The shared Prism window: every mod's log and state");
            Widgets.Action(body, "Reload Bismuth", "Reload", MainClass.RequestForceReload,
                           "Rebuilds everything without restarting the game");

            var updates = Widgets.Section(body, "Updates");
            Widgets.Choice(updates, "Channel", new[] { "Stable", "Beta" },
                           string.Equals(S.UpdateChannel, "beta", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                           i => { S.UpdateChannel = i == 1 ? "beta" : ""; Apply(); },
                           "Beta offers prereleases as well");
            Widgets.Action(updates, "Check for updates", "Check now", UpdateChecker.CheckNow);
            Widgets.Action(updates, "Releases page", "Open",
                           () => Application.OpenURL(UpdateChecker.ReleasesPage));

            // The dump tools only mean anything with debug mode on, so they live under it.
            var dev = Widgets.ToggleGroup(body, "Debug mode", S.DebugMode,
                                          v => { S.DebugMode = v; Apply(); },
                                          "Extra logging, and the tools below");
            Widgets.Toggle(dev, "Trace font sweep", GameFontApplier.DiagEnabled,
                           v => GameFontApplier.DiagEnabled = v,
                           "Logs every text object the font sweep touches");
            Widgets.Text(dev, "Filter", GameProbe.Filter, v => GameProbe.Filter = v,
                         "Only dump objects whose name contains this");
            var dumps = Widgets.Section(dev, "Dump");
            Widgets.Action(dumps, "Texts", "Dump", GameProbe.DumpTexts);
            Widgets.Action(dumps, "Images", "Dump", GameProbe.DumpImages);
            Widgets.Action(dumps, "Assets", "Dump", GameProbe.DumpAssets);
        }
    }
}
