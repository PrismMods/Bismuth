using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bismuth.UI.Pages
{
    internal static class PageKeyViewer
    {
        // Page-lifetime navigation state. The page is built once per session by TabRail;
        // subpage bodies are (re)built by the PageStack on push/reveal.
        private static PageStack _stack;
        private static RectTransform _editorBody;   // current preset-editor body — drag-ghost host
        private static Action _listRebuildAll;

        // Rebind state: when a cell is right-clicked, we capture the next keydown into
        // this cell. The KeyListener lives on the editor view's GameObject.
        private static KeyViewerCell _rebindCell;
        private static KeyListener _rebindListener;
        private static Action _rebindRebuild;

        public static void Build(PageStack stack)
        {
            _stack = stack;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            Action rebuild = () => UICore.OnKeyViewerRebuild?.Invoke();

            // Drop rebuild hooks accumulated by a previous panel build (force reload).
            _listRebuildAll = null;
            // Preset names can change while an editor subpage is open.
            stack.OnRootRevealed += () => _listRebuildAll?.Invoke();

            BuildListView(stack.Root, s, notify, rebuild);
        }

        private static void BuildListView(Transform parent, Settings s, Action notify, Action rebuild)
        {
            UIBuilder.SectionHeader(parent, "Key Viewer");
            UIBuilder.Collapsible(parent, "Enable", s.ShowKeyViewer,
                v => { s.ShowKeyViewer = v; notify?.Invoke(); rebuild(); }, null);
            UIBuilder.Collapsible(parent, "Hide in level editor", s.HideKeyViewerInEditor,
                v => { s.HideKeyViewerInEditor = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(parent, "Hide in main menu", s.HideKeyViewerInMainMenu,
                v => { s.HideKeyViewerInMainMenu = v; notify?.Invoke(); }, null);
            PageUI.BuildFontSelector(parent, "Font", UICore.AvailableFonts, s.KeyViewerFontName,
                entry =>
                {
                    s.KeyViewerFontName = entry.Name;
                    MainClass.ApplySelectedFont();
                    notify?.Invoke();
                    PageOverlay.RefreshFontWeightRows?.Invoke();
                }, showWeightRow: false);
            // Weight rows only show when the KV font's family has multiple weights.
            // NOTE: relies on the Overlay tab building first (it resets
            // PageOverlay.RefreshFontWeightRows at the top of its Build).
            PageOverlay.AddWeightRow(parent, "Label weight",
                () => s.KeyViewerLabelWeight, v => s.KeyViewerLabelWeight = v,
                fontName: () => s.EffectiveKeyViewerFont);
            PageOverlay.AddWeightRow(parent, "Count weight",
                () => s.KeyViewerCountWeight, v => s.KeyViewerCountWeight = v,
                fontName: () => s.EffectiveKeyViewerFont);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Hand");
            UIBuilder.Collapsible(parent, "Enabled", s.ShowHandViewer,
                v => { s.ShowHandViewer = v; notify?.Invoke(); rebuild(); }, null);
            BuildPresetList(parent, isFoot: false, s, notify, rebuild);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Foot");
            UIBuilder.Collapsible(parent, "Enabled", s.ShowFootViewer,
                v => { s.ShowFootViewer = v; notify?.Invoke(); rebuild(); }, null);
            BuildPresetList(parent, isFoot: true, s, notify, rebuild);
        }

        private static void BuildPresetList(Transform parent, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            var listGo = UIBuilder.Rect(isFoot ? "FootPresets" : "HandPresets", parent);
            var lvlg = listGo.AddComponent<VerticalLayoutGroup>();
            lvlg.childControlWidth = true;
            lvlg.childControlHeight = true;
            lvlg.childForceExpandWidth = true;
            lvlg.childForceExpandHeight = false;
            lvlg.spacing = 2f;

            Action listRebuild = null;
            listRebuild = () =>
            {
                for (int i = listGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = listGo.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
                if (presets == null) return;
                for (int i = 0; i < presets.Count; i++)
                    BuildPresetRow(listGo.transform, isFoot, i, presets[i], s, notify, rebuild, listRebuild);
            };
            listRebuild();
            // Combine all preset-list rebuilds so returning to the root refreshes both hand and foot.
            _listRebuildAll = (_listRebuildAll ?? (Action)delegate { }) + listRebuild;

            string label = isFoot ? Loc.T("+ Add Foot Preset") : Loc.T("+ Add Hand Preset");
            UIBuilder.Button(parent, label, () =>
            {
                var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
                if (presets == null) return;
                string nm = (isFoot ? "Foot" : "Hand") + " " + (presets.Count + 1);
                var np = new KeyViewerPreset { Name = nm };
                np.EnsureDefaults();
                presets.Add(np);
                listRebuild();
                notify?.Invoke();
                rebuild();
            });
        }

        private static void BuildPresetRow(
            Transform parent, bool isFoot, int idx, KeyViewerPreset preset,
            Settings s, Action notify, Action rebuild, Action listRebuild)
        {
            int active = isFoot ? s.KvActiveFoot : s.KvActiveHand;
            bool isActive = idx == active;

            var row = UIBuilder.Rect("Preset_" + idx, parent);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = UIBuilder.RowHeight;
            rowLe.minHeight = UIBuilder.RowHeight;
            var rowBg = UIBuilder.SolidImage(row, new Color(0, 0, 0, 0));
            rowBg.raycastTarget = true;

            const float ringSize = 14f;
            const float dotSize = 6f;
            const float editW = 50f;
            const float delW = 32f;
            const float buttonGap = 4f;

            var ringGo = UIBuilder.Rect("Ring", row.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(0, 0.5f);
            ringRect.anchorMax = new Vector2(0, 0.5f);
            ringRect.pivot = new Vector2(0, 0.5f);
            ringRect.anchoredPosition = new Vector2(8f, 0);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = isActive ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = true;
            var ringAccent = ringGo.AddComponent<AccentBorder>();
            ringAccent.Active = isActive;

            var dotGo = UIBuilder.Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.AddComponent<AccentFill>();
            dotGo.SetActive(isActive);

            float rightCluster = editW + delW + buttonGap * 3 + 8f;
            var nameGo = UIBuilder.Rect("Name", row.transform);
            var nameRect = (RectTransform)nameGo.transform;
            nameRect.anchorMin = new Vector2(0, 0);
            nameRect.anchorMax = new Vector2(1, 1);
            nameRect.offsetMin = new Vector2(8f + ringSize + 8f, 4f);
            nameRect.offsetMax = new Vector2(-rightCluster, -4f);
            var nameBg = UIBuilder.SolidImage(nameGo, new Color(1, 1, 1, 0.04f));
            nameBg.raycastTarget = true;

            var nameTxtGo = UIBuilder.Rect("T", nameGo.transform);
            var nameTxtRect = (RectTransform)nameTxtGo.transform;
            nameTxtRect.anchorMin = Vector2.zero;
            nameTxtRect.anchorMax = Vector2.one;
            nameTxtRect.offsetMin = new Vector2(8f, 0);
            nameTxtRect.offsetMax = new Vector2(-8f, 0);
            var nameTxt = UIBuilder.Tmp(nameTxtGo, "", (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
            nameTxt.richText = false;

            var nameInput = UIBuilder.BuildInputField(nameGo, nameTxt);
            nameInput.contentType = TMP_InputField.ContentType.Standard;
            nameInput.lineType = TMP_InputField.LineType.SingleLine;
            nameInput.text = preset.Name ?? "";
            nameInput.onEndEdit.AddListener(v =>
            {
                preset.Name = v;
                notify?.Invoke();
            });

            // Edit — opens the editor view
            Action openEditor = () => OpenEditor(preset, isFoot, s, notify, rebuild);
            var editBtn = MakeMiniButton(row.transform, "Edit", editW,
                anchoredX: -(delW + buttonGap * 2 + 8f),
                onClick: openEditor);

            /* This row is a bespoke widget, not a NavRow, so nothing registered it — which
               left EVERY key viewer setting below it unsearchable (a user hunting "rain
               color" had to know it lives under a row). Register the preset as the search
               entry point with its subpages' contents as keywords, same as NavRow does. */
            SettingsSearch.Register((isFoot ? Loc.T("Foot preset: ") : Loc.T("Hand preset: ")) + (preset.Name ?? "?"),
                openEditor, EditorKeywords);

            // Delete (disabled when only 1 preset)
            var presets = isFoot ? s.KvFootPresets : s.KvHandPresets;
            bool canDelete = presets != null && presets.Count > 1;
            var delBtn = MakeMiniButton(row.transform, "×", delW,
                anchoredX: -8f,
                onClick: canDelete ? new Action(() =>
                {
                    presets.RemoveAt(idx);
                    int newActive = active >= presets.Count ? presets.Count - 1 : active;
                    if (isFoot) s.KvActiveFoot = newActive;
                    else        s.KvActiveHand = newActive;
                    listRebuild();
                    notify?.Invoke();
                    rebuild();
                }) : null);
            if (!canDelete)
            {
                var delBg = delBtn.GetComponent<RoundedRectGraphic>();
                delBg.color = new Color(Theme.ButtonBg.r, Theme.ButtonBg.g, Theme.ButtonBg.b, 0.04f);
                delBg.raycastTarget = false;
                delBtn.GetComponentInChildren<TextMeshProUGUI>().color = Theme.TextMuted;
            }

            Action select = () =>
            {
                if (isFoot) s.KvActiveFoot = idx;
                else        s.KvActiveHand = idx;
                listRebuild();
                notify?.Invoke();
                rebuild();
            };
            ClickHandler.Attach(ringGo, select);
            ClickHandler.Attach(row, select);
        }

        // Mini button used for Edit / Delete / Back. Compact 22-tall pill with a label.
        private static GameObject MakeMiniButton(Transform parent, string label, float width, float anchoredX, Action onClick)
        {
            var btn = UIBuilder.Rect(label, parent);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = new Vector2(1, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(anchoredX, 0);
            rect.sizeDelta = new Vector2(width, 22f);

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.ButtonBg;
            bg.raycastTarget = true;

            var lblGo = UIBuilder.Rect("L", btn.transform);
            var lblRect = (RectTransform)lblGo.transform;
            lblRect.anchorMin = Vector2.zero;
            lblRect.anchorMax = Vector2.one;
            lblRect.offsetMin = Vector2.zero;
            lblRect.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(lblGo, label, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            if (onClick != null) ClickHandler.Attach(btn, onClick);
            return btn;
        }

        /* Contents of the preset editor and its row/cell subpages, so search can reach
           settings that only exist behind a drill-in. Keep in step with the labels those
           pages build — `grep -oE '(Slider|Collapsible|ColorPicker|BindKv)\(body[^,]*, "[^"]+"'`
           over this file lists them. */
        private const string EditorKeywords =
            "key width,radius,border,gap,scale,position,x,y,"
            + "rain,rain color,key rain,fade start,track length,speed,width step,"
            + "shadow size,shadow color,background,label text,count text,font size,"
            + "persist counts,reset counters,ghost keys,custom rain color,style,"
            + "glow size,glow tint,corner radius,released,pressed,"
            + "row,row height,show rain,add row,cell,key,width,visible,"
            + "hide in main menu,hide in level editor";

        // ── Editor view ────────────────────────────────────────────────────

        private static void OpenEditor(KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            // rebuildOnReveal: the row/cell submenus mutate the grid underneath, so the
            // editor re-reads the preset when they pop back to it.
            _stack.Push((isFoot ? Loc.T("Foot / ") : Loc.T("Hand / ")) + preset.Name,
                body => BuildEditorContent(body, preset, isFoot, s, notify, rebuild),
                rebuildOnReveal: true);
        }

        private static void BuildEditorContent(Transform parent, KeyViewerPreset preset, bool isFoot, Settings s, Action notify, Action rebuild)
        {
            _editorBody = (RectTransform)parent;
            // Stale hook from a previously-opened editor would target destroyed objects;
            // clear before the rows section's initial rebuild fires it.
            _ghostRefresh = null;

            // Combined callback for structural fields. Cosmetic-only fields use notify.
            Action structural = () => { notify?.Invoke(); rebuild(); };

            // Name + Reset Counters
            UIBuilder.TextInput(parent, "Name", preset.Name ?? "",
                v => { preset.Name = v; _stack.RetitleTop((isFoot ? Loc.T("Foot / ") : Loc.T("Hand / ")) + v); notify?.Invoke(); });
            UIBuilder.DangerButton(parent, "Reset counters for this preset", () =>
            {
                if (KeyViewer.Instance != null)
                {
                    KeyViewer.Instance.ResetCounts();
                    notify?.Invoke();
                }
            });

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeader(parent, "Main");
            UIBuilder.Slider(parent, "Key width", preset.KeyWidth, 20f, 200f,
                v => { preset.KeyWidth = v; structural(); }, "0", 1f);
            UIBuilder.Slider(parent, "Gap", preset.Gap, 0f, 30f,
                v => { preset.Gap = v; structural(); }, "0", 1f);
            UIBuilder.Slider(parent, "X", preset.X, 0f, 1f,
                v => { preset.X = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(parent, "Y", preset.Y, 0f, 1f,
                v => { preset.Y = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Slider(parent, "Scale", preset.Scale, 0.25f, 3f,
                v => { preset.Scale = v; notify?.Invoke(); }, "0.00");
            UIBuilder.Collapsible(parent, "Persist counts", preset.PersistCounts,
                v => { preset.PersistCounts = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeaderWithHelp(parent, "Rows",
                "Rebind mode: click keys and press their new binds.\n" +
                "Click: cell settings (bind, display text, width)\n" +
                "Right Click: change key bind\n" +
                "Drag: change key position\n" +
                "Click Settings on a row for height + rain options.");
            BuildRebindModeButton(parent);
            BuildRowsSection(parent, preset, isFoot, s, notify, rebuild);

            UIBuilder.Spacer(parent);
            UIBuilder.SectionHeaderWithHelp(parent, "Style",
                "Click a card to turn that part on or off\n"
                + "(highlighted = on).\n"
                + "Click the ··· button on a card for its settings.");

            var grid = UIBuilder.CardGrid(parent).transform;

            UIBuilder.NavCard(grid, "Background", preset.ShowBackground,
                v => { preset.ShowBackground = v; structural(); },
                () => _stack.Push("Background", body =>
            {
                EnsureKv(ref preset.BgIdle, 0, 0, 0, 0.7f);
                EnsureKv(ref preset.BgHeld, 1, 1, 1, 1);
                BindKv(body, "Released", preset.BgIdle, notify);
                BindKv(body, "Pressed",  preset.BgHeld, notify);
            }));

            UIBuilder.NavCard(grid, "Border", preset.ShowBorder,
                v => { preset.ShowBorder = v; structural(); },
                () => _stack.Push("Border", body =>
            {
                UIBuilder.IntSlider(body, "Radius", preset.Radius, 0, 64,
                    v => { preset.Radius = v; structural(); });
                UIBuilder.Slider(body, "Width", preset.BorderWidth, 0f, 16f,
                    v => { preset.BorderWidth = v; structural(); }, "0.0", 0.5f);
                EnsureKv(ref preset.BorderIdle, 1, 1, 1, 1);
                EnsureKv(ref preset.BorderHeld, 1, 1, 1, 1);
                BindKv(body, "Released", preset.BorderIdle, notify);
                BindKv(body, "Pressed",  preset.BorderHeld, notify);
            }));

            UIBuilder.NavCard(grid, "Label Text", preset.ShowLabel,
                v => { preset.ShowLabel = v; structural(); },
                () => _stack.Push("Label Text", body =>
                {
                    UIBuilder.IntSlider(body, "Font size", preset.LabelSize, 6, 48,
                        v => { preset.LabelSize = v; notify?.Invoke(); });
                    EnsureKv(ref preset.TxtIdle, 1, 1, 1, 1);
                    EnsureKv(ref preset.TxtHeld, 0, 0, 0, 1);
                    BindKv(body, "Released", preset.TxtIdle, notify);
                    BindKv(body, "Pressed",  preset.TxtHeld, notify);
                }));

            UIBuilder.NavCard(grid, "Count Text", preset.ShowCount,
                v => { preset.ShowCount = v; structural(); },
                () => _stack.Push("Count Text", body =>
                {
                    UIBuilder.IntSlider(body, "Font size", preset.CountSize, 6, 48,
                        v => { preset.CountSize = v; notify?.Invoke(); });
                    EnsureKv(ref preset.CountIdle, 0.7f, 0.7f, 0.7f, 1);
                    EnsureKv(ref preset.CountHeld, 0, 0, 0, 1);
                    BindKv(body, "Released", preset.CountIdle, notify);
                    BindKv(body, "Pressed",  preset.CountHeld, notify);
                }));

            UIBuilder.NavCard(grid, "Key Rain", preset.ShowRain,
                v => { preset.ShowRain = v; structural(); },
                () => _stack.Push("Key Rain", body =>
            {
                UIBuilder.SectionHeader(body, "Track");
                UIBuilder.Slider(body, "Track length", preset.RainTrackLength, 50f, 1000f,
                    v => { preset.RainTrackLength = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(body, "Fade start", preset.RainDistance, 0f, 1000f,
                    v => { preset.RainDistance = v; notify?.Invoke(); }, "0", 1f);
                UIBuilder.Slider(body, "Speed (px/sec)", preset.RainSpeed, 50f, 2000f,
                    v => { preset.RainSpeed = v; notify?.Invoke(); }, "0", 10f);

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeader(body, "Shape");
                UIBuilder.Slider(body, "Width step", preset.RainWidthStep, 0f, 30f,
                    v => { preset.RainWidthStep = v; notify?.Invoke(); }, "0.0", 0.5f);
                UIBuilder.IntSlider(body, "Corner radius", preset.RainRadius, 0, 20,
                    v => { preset.RainRadius = v; notify?.Invoke(); });

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeader(body, "Shadow");
                UIBuilder.Slider(body, "Size", preset.RainShadowSize, 0f, 40f,
                    v => { preset.RainShadowSize = v; notify?.Invoke(); }, "0.0", 0.5f);
                EnsureKv(ref preset.RainShadowColor, 0, 0, 0, 0.05f);
                BindKv(body, "Color", preset.RainShadowColor, notify);

                UIBuilder.Spacer(body);
                UIBuilder.SectionHeaderWithHelp(body, "Glow",
                    "The tint multiplies the rain color, so white glows\n"
                    + "in each key's own color.");
                UIBuilder.Slider(body, "Size", preset.RainGlowSize, 0f, 60f,
                    v => { preset.RainGlowSize = v; notify?.Invoke(); }, "0.0", 0.5f);
                EnsureKv(ref preset.RainGlowColor, 1, 1, 1, 0.5f);
                BindKv(body, "Tint", preset.RainGlowColor, notify);
            }));

            // Ghost Keys — hand presets only. Foot doesn't use them. The card carries the
            // enable flag, so the subpage is just the slots and their rain color.
            if (!isFoot)
            {
                UIBuilder.NavCard(grid, "Ghost Keys", preset.GhostKeysEnabled,
                    v => { preset.GhostKeysEnabled = v; structural(); },
                    () => _stack.Push("Ghost Keys", body =>
                    {
                        UIBuilder.SectionHeaderWithHelp(body, "Slots",
                            "Ghost keys spawn rain at the matching top-row position\n"
                            + "but don't count as input.\n"
                            + "Withholding them from the game needs the key limiter\n"
                            + "on (Input tab) — with it off, they still spawn rain\n"
                            + "but also hit tiles.");
                        BuildGhostSection(body, preset, notify, rebuild);
                    }));
            }
        }

        // ── Ghost keys ─────────────────────────────────────────────────────

        // Re-syncs the ghost slot chips when the top row's cells change (slot count is
        // derived from the top row). Set while a hand preset's editor is open.
        private static Action _ghostRefresh;

        private static int TopRowKeySlots(KeyViewerPreset preset)
        {
            if (preset?.Rows == null || preset.Rows.Count == 0) return 0;
            var row = preset.Rows[0];
            if (row?.Cells == null) return 0;
            int n = 0;
            foreach (var c in row.Cells)
                if (c.Token != "KPS" && c.Token != "Total") n++;
            return n;
        }

        private static void BuildGhostSection(Transform parent, KeyViewerPreset preset, Action notify, Action rebuild)
        {
            Action structural = () => { notify?.Invoke(); rebuild(); };

            var body = UIBuilder.Rect("GhostBody", parent);
            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;

            // One chip per non-stat top-row cell. Click empty → listen; click assigned → clear.
            var stripGo = UIBuilder.Rect("Slots", body.transform);
            var stripLe = stripGo.AddComponent<LayoutElement>();
            stripLe.preferredHeight = 32f;
            stripLe.minHeight = 32f;
            var hlg = stripGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(8, 0, 4, 4);

            var listenerGo = UIBuilder.Rect("GhostListener", body.transform);
            var listener = listenerGo.AddComponent<KeyListener>();

            int listenIdx = -1;
            Action rebuildSlots = null;
            rebuildSlots = () =>
            {
                for (int i = stripGo.transform.childCount - 1; i >= 0; i--)
                {
                    var c = stripGo.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }

                int slots = TopRowKeySlots(preset);
                if (preset.GhostKeys == null) preset.GhostKeys = new List<string>();
                while (preset.GhostKeys.Count < slots) preset.GhostKeys.Add("None");
                while (preset.GhostKeys.Count > slots) preset.GhostKeys.RemoveAt(preset.GhostKeys.Count - 1);
                if (listenIdx >= slots) { listenIdx = -1; listener.Active = false; }

                if (slots == 0)
                {
                    MakeGhostChip(stripGo.transform, Loc.T("(top row has no key cells)"), false, null);
                    return;
                }

                for (int i = 0; i < slots; i++)
                {
                    int si = i;
                    string tok = preset.GhostKeys[si] ?? "None";
                    bool assigned = tok != "None" && !string.IsNullOrEmpty(tok);
                    bool listeningThis = listenIdx == si;
                    string label = listeningThis ? "…" : (assigned ? KeyTokens.PrettyTokenLabel(tok) : "None");
                    var chipGo = MakeGhostChip(stripGo.transform, label, listeningThis, () =>
                    {
                        if (listeningThis) { listenIdx = -1; listener.Active = false; }
                        else if (assigned)
                        {
                            preset.GhostKeys[si] = "None";
                            structural();
                        }
                        else { listenIdx = si; listener.Active = true; }
                        rebuildSlots();
                    });
                    if (listeningThis) listener.CancelRect = (RectTransform)chipGo.transform;
                }
            };
            listener.OnKey = kc =>
            {
                if (listenIdx < 0) return;
                if (kc != KeyCode.Escape && listenIdx < preset.GhostKeys.Count)
                {
                    preset.GhostKeys[listenIdx] = KeyTokens.TokenFromKeyCode(kc);
                    structural();
                }
                listenIdx = -1;
                listener.Active = false;
                rebuildSlots();
            };
            rebuildSlots();
            _ghostRefresh = rebuildSlots;

            // Rain color defaults to yellow when unset (null). The picker binds one persistent
            // KvColor; the toggle points GhostRainColor at it or back to null, so edits
            // survive toggling custom off and on.
            var ghostCol = preset.GhostRainColor ?? new KvColor { R = 1f, G = 0.9f, B = 0f, A = 1f };
            GameObject pickerGo = null;
            UIBuilder.Collapsible(body.transform, "Custom rain color", preset.GhostRainColor != null,
                v =>
                {
                    preset.GhostRainColor = v ? ghostCol : null;
                    if (pickerGo != null) pickerGo.SetActive(v);
                    structural();
                }, null);
            pickerGo = UIBuilder.ColorPicker(body.transform, "Rain color",
                new Color(ghostCol.R, ghostCol.G, ghostCol.B, ghostCol.A), true,
                c =>
                {
                    ghostCol.R = c.r; ghostCol.G = c.g; ghostCol.B = c.b; ghostCol.A = c.a;
                    notify?.Invoke();
                });
            pickerGo.SetActive(preset.GhostRainColor != null);
        }

        private static GameObject MakeGhostChip(Transform parent, string text, bool active, Action onClick)
        {
            var go = UIBuilder.Rect("Chip", parent);
            float width = Mathf.Max(36f, text.Length * 8f + 14f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 24f;
            le.minWidth = width;
            le.minHeight = 24f;

            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = active ? Theme.ToggleOn : Theme.ButtonBg;
            bg.raycastTarget = onClick != null;
            if (active) go.AddComponent<AccentFill>();

            var txtGo = UIBuilder.Rect("T", go.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(6f, 0f);
            txtRect.offsetMax = new Vector2(-6f, 0f);
            var txt = UIBuilder.Tmp(txtGo, text, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter,
                onClick != null ? Theme.Text : Theme.TextMuted);

            if (onClick != null) ClickHandler.Attach(go, onClick);
            return go;
        }

        // ── Rebind mode ────────────────────────────────────────────────────
        // While on, clicking a cell arms it for rebinding (next keypress binds it)
        // instead of opening its settings — bulk-friendly: click key, press bind, next.
        private static bool _rebindMode;

        private static void BuildRebindModeButton(Transform parent)
        {
            _rebindMode = false;
            var row = UIBuilder.Row(parent);
            var bg = UIBuilder.SolidImage(row, Theme.ButtonBg);
            bg.raycastTarget = true;
            var label = UIBuilder.Label(row.transform, "", (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            bool hover = false;
            void Paint()
            {
                if (_rebindMode)
                {
                    var a = Theme.ToggleOn;
                    bg.color = new Color(a.r, a.g, a.b, hover ? 0.5f : 0.35f);
                    label.text = Loc.T("Rebind mode ON — click a key, press its new bind. Click here to finish.");
                }
                else
                {
                    bg.color = hover ? Theme.ButtonHover : Theme.ButtonBg;
                    label.text = Loc.T("Rebind Keys");
                }
            }
            Paint();

            var h = row.AddComponent<HoverHandler>();
            h.OnEnter = () => { hover = true; Paint(); };
            h.OnExit = () => { hover = false; Paint(); };
            ClickHandler.Attach(row, () =>
            {
                _rebindMode = !_rebindMode;
                if (!_rebindMode)
                {
                    // Leaving the mode cancels any armed cell.
                    _rebindCell = null;
                    if (_rebindListener != null) _rebindListener.Active = false;
                    _rebindRebuild?.Invoke();
                }
                Paint();
            });
        }

        // ── Rows section + cell grid ───────────────────────────────────────

        // Visual row grid: each row is a horizontal strip of cell buttons + a Row Settings
        // button on the right. Click cell → key submenu. Right-click cell → listen-rebind.
        // Click row settings → row submenu. Drag-reorder is deferred.
        private static void BuildRowsSection(
            Transform parent, KeyViewerPreset preset, bool isFoot,
            Settings s, Action notify, Action rebuild)
        {
            // KeyListener for rebind, attached once per editor build. Its OnKey is wired
            // by the right-click handlers and rewires the rebind state on each capture.
            var listenerGo = UIBuilder.Rect("RebindListener", parent);
            _rebindListener = listenerGo.AddComponent<KeyListener>();
            _rebindCell = null;
            _rebindRebuild = null;
            _rebindListener.OnKey = kc =>
            {
                if (_rebindCell == null) return;
                if (kc == KeyCode.Escape)
                {
                    // Cancel rebind without changing anything
                    _rebindCell = null;
                    _rebindListener.Active = false;
                    _rebindRebuild?.Invoke();
                    return;
                }
                // Swap the token first so TransferKeyCount's "is oldKey still in use" scan
                // sees the new binding, else it spots this cell still on the old token and
                // leaves the old count behind.
                bool hadOld = KeyViewer.TryParseKey(_rebindCell.Token, out KeyCode oldKey);
                _rebindCell.Token = KeyTokens.TokenFromKeyCode(kc);
                _rebindCell.Label = null; // clear stale override
                if (hadOld && KeyViewer.Instance != null)
                    KeyViewer.Instance.TransferKeyCount(preset, oldKey, kc);
                _rebindCell = null;
                _rebindListener.Active = false;
                _rebindRebuild?.Invoke();
                notify?.Invoke();
                rebuild();
            };

            var rowsContainer = UIBuilder.Rect("RowsContainer", parent);
            var rvlg = rowsContainer.AddComponent<VerticalLayoutGroup>();
            rvlg.childControlWidth = true;
            rvlg.childControlHeight = true;
            rvlg.childForceExpandWidth = true;
            rvlg.childForceExpandHeight = false;
            rvlg.spacing = 4f;

            Action rebuildRows = null;
            rebuildRows = () =>
            {
                for (int i = rowsContainer.transform.childCount - 1; i >= 0; i--)
                {
                    var c = rowsContainer.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                if (preset.Rows == null) return;
                for (int i = 0; i < preset.Rows.Count; i++)
                {
                    BuildRowStrip(rowsContainer.transform, preset, isFoot, i, s, notify, rebuild, rebuildRows);
                }
                // Ghost slot count derives from the top row's cells — keep the chips in sync.
                _ghostRefresh?.Invoke();
            };
            _rebindRebuild = rebuildRows;
            rebuildRows();

            UIBuilder.Spacer(parent, 8f);
            UIBuilder.Button(parent, "+ Add Row", () =>
            {
                if (preset.Rows == null) preset.Rows = new List<KeyViewerRow>();
                var newRow = new KeyViewerRow { Cells = new List<KeyViewerCell>(), Height = 60f, ShowRain = true };
                preset.Rows.Add(newRow);
                rebuildRows();
                notify?.Invoke();
                rebuild();
            });
        }

        private static void BuildRowStrip(
            Transform parent, KeyViewerPreset preset, bool isFoot, int rowIdx,
            Settings s, Action notify, Action rebuild, Action rebuildRows)
        {
            const float cellH = 32f;
            // Reserved horizontal space for the right cluster (+ KPS Total Settings + gaps).
            const float rightClusterReserve = 280f;

            var row = preset.Rows[rowIdx];

            var stripGo = UIBuilder.Rect("Row_" + rowIdx, parent);
            var stripLe = stripGo.AddComponent<LayoutElement>();
            stripLe.preferredHeight = cellH + 4f;
            stripLe.minHeight = cellH + 4f;

            // Cells container — left-anchored, only data cells. The + / KPS / Total /
            // Settings buttons sit in the right cluster so drag-reorder needn't skip them.
            var cellsGo = UIBuilder.Rect("Cells", stripGo.transform);
            var cellsRect = (RectTransform)cellsGo.transform;
            cellsRect.anchorMin = new Vector2(0, 0);
            cellsRect.anchorMax = new Vector2(1, 1);
            cellsRect.offsetMin = new Vector2(8f, 2f);
            cellsRect.offsetMax = new Vector2(-rightClusterReserve, -2f);
            var cellsHlg = cellsGo.AddComponent<HorizontalLayoutGroup>();
            cellsHlg.childControlWidth = true;
            cellsHlg.childControlHeight = true;
            cellsHlg.childForceExpandWidth = false;
            cellsHlg.childForceExpandHeight = false;
            cellsHlg.childAlignment = TextAnchor.MiddleLeft;
            cellsHlg.spacing = 2f;

            if (row.Cells != null)
            {
                for (int j = 0; j < row.Cells.Count; j++)
                {
                    BuildCellButton(cellsGo.transform, preset, isFoot, rowIdx, j, row.Cells[j], s, notify, rebuild, rebuildRows);
                }
            }

            // Right cluster: + / + KPS / + Total / ⚙ Settings, packed against the right edge.
            var rightCluster = UIBuilder.Rect("RightCluster", stripGo.transform);
            var rcRect = (RectTransform)rightCluster.transform;
            rcRect.anchorMin = new Vector2(1, 0);
            rcRect.anchorMax = new Vector2(1, 1);
            rcRect.pivot = new Vector2(1, 0.5f);
            rcRect.anchoredPosition = new Vector2(-8f, 0);
            rcRect.sizeDelta = new Vector2(0, 0);
            var rcHlg = rightCluster.AddComponent<HorizontalLayoutGroup>();
            rcHlg.childControlWidth = true;
            rcHlg.childControlHeight = true;
            rcHlg.childForceExpandWidth = false;
            rcHlg.childForceExpandHeight = false;
            rcHlg.childAlignment = TextAnchor.MiddleRight;
            rcHlg.spacing = 4f;
            // Auto-size width to fit children; right-anchored placement keeps it pinned.
            var rcCsf = rightCluster.AddComponent<ContentSizeFitter>();
            rcCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildAddCellButton(rightCluster.transform, row, rebuildRows);
            BuildAddSpecialButton(rightCluster.transform, row, "KPS", rebuildRows);
            BuildAddSpecialButton(rightCluster.transform, row, "Total", rebuildRows);
            BuildRowSettingsButton(rightCluster.transform, preset, isFoot, rowIdx, s, notify, rebuild);
        }

        // Row Settings button (HLG-positioned, no manual anchoredPosition).
        private static void BuildRowSettingsButton(Transform parent, KeyViewerPreset preset, bool isFoot, int rowIdx, Settings s, Action notify, Action rebuild)
        {
            const float w = 84f;
            var btn = UIBuilder.Rect(Loc.T("Settings"), parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.ButtonBg;
            bg.raycastTarget = true;

            var lblGo = UIBuilder.Rect("L", btn.transform);
            var lblRect = (RectTransform)lblGo.transform;
            lblRect.anchorMin = Vector2.zero;
            lblRect.anchorMax = Vector2.one;
            lblRect.offsetMin = Vector2.zero;
            lblRect.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(lblGo, "Settings", (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.Text);

            ClickHandler.Attach(btn, () => OpenRowSubmenu(preset, isFoot, rowIdx, s, notify, rebuild));
        }

        // Per-cell button. Label = override if set, else PrettyTokenLabel(Token). Click →
        // key submenu, right-click → enter rebind state for this cell (visual feedback via
        // accent tint until next keypress or Esc).
        private static void BuildCellButton(
            Transform parent, KeyViewerPreset preset, bool isFoot,
            int rowIdx, int cellIdx, KeyViewerCell cell,
            Settings s, Action notify, Action rebuild, Action rebuildRows)
        {
            float w = Mathf.Max(28f, cell.WidthMul * 40f);
            var btn = UIBuilder.Rect("Cell_" + cellIdx, parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            bool isRebinding = (cell == _rebindCell);
            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            bg.color = isRebinding ? Theme.ToggleOn : Theme.ButtonBg;
            bg.raycastTarget = true;
            if (isRebinding)
            {
                btn.AddComponent<AccentFill>();
                // Re-pointed on every rebuild, so clicking the armed cell cancels rather
                // than binding a mouse button to it.
                _rebindListener.CancelRect = (RectTransform)btn.transform;
            }

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(2f, 0);
            txtRect.offsetMax = new Vector2(-2f, 0);
            var txt = UIBuilder.Tmp(txtGo, "", (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.Text);
            // Auto-size so long labels (Space, Total, RAlt…) shrink instead of wrapping
            // into two lines and clipping. Short labels still render at the normal size.
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 8;
            txt.fontSizeMax = (int)UIBuilder.LabelFontSize - 1;
            txt.overflowMode = TextOverflowModes.Truncate;
            txt.text = isRebinding ? "…"
                : (!string.IsNullOrEmpty(cell.Label) ? cell.Label : KeyTokens.PrettyTokenLabel(cell.Token));

            // Left click: cell settings, or arm-for-rebind while rebind mode is on.
            // Right click: always arm-for-rebind.
            Action armRebind = () =>
            {
                // Cancel any prior pending rebind, then enter rebind state for this cell.
                _rebindCell = cell;
                _rebindListener.Active = true;
                rebuildRows();
            };
            var ch = ClickHandler.Attach(btn, () =>
            {
                if (_rebindMode) armRebind();
                else OpenKeySubmenu(preset, isFoot, rowIdx, cellIdx, s, notify, rebuild);
            });
            ch.OnRightClick = armRebind;

            // Drag-reorder. Cross-row drops route through Preset.Rows lookup in the handler.
            var dr = btn.AddComponent<CellDragReorder>();
            dr.Cell = cell;
            dr.Row = preset.Rows[rowIdx];
            dr.Preset = preset;
            dr.CellsContainer = (RectTransform)parent;
            dr.GhostHost = _editorBody;
            // After reorder: rebuild the editor's grid AND fire the live KeyViewer rebuild
            // so the overlay reflects the new cell order immediately.
            dr.OnReorder = () =>
            {
                rebuildRows();
                notify?.Invoke();
                rebuild();
            };
        }

        // Inline "+ add cell" button at the end of each row strip. Adds an empty-token cell
        // and immediately enters rebind state for it, so the next keypress sets the token.
        private static void BuildAddCellButton(Transform parent, KeyViewerRow row, Action rebuildRows)
        {
            const float w = 28f;
            var btn = UIBuilder.Rect("AddCell", parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            // Half-alpha button bg — visually distinct from regular cells so it reads as
            // an affordance rather than another key.
            var c = Theme.ButtonBg; c.a *= 0.5f;
            bg.color = c;
            bg.raycastTarget = true;

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(txtGo, "+", (int)UIBuilder.LabelFontSize + 2, TextAnchor.MiddleCenter, Theme.TextMuted);

            ClickHandler.Attach(btn, () =>
            {
                if (row.Cells == null) row.Cells = new List<KeyViewerCell>();
                var newCell = new KeyViewerCell { Token = "", WidthMul = 1f };
                row.Cells.Add(newCell);
                // Immediately listen-rebind the new cell — next keypress sets its token.
                _rebindCell = newCell;
                if (_rebindListener != null) _rebindListener.Active = true;
                rebuildRows();
            });
        }

        // Inline button that inserts a special-token cell (KPS or Total). No rebind step
        // since these aren't keyboard keys — they're computed by the runtime.
        private static void BuildAddSpecialButton(Transform parent, KeyViewerRow row, string token, Action rebuildRows)
        {
            float w = token.Length * 9f + 18f;
            var btn = UIBuilder.Rect("Add" + token, parent);
            var le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 32f;
            le.minWidth = w;
            le.minHeight = 32f;

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f;
            bg.AAFringe = 0.5f;
            var c = Theme.ButtonBg; c.a *= 0.5f;
            bg.color = c;
            bg.raycastTarget = true;

            var txtGo = UIBuilder.Rect("L", btn.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(txtGo, "+ " + token, (int)UIBuilder.LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.TextMuted);

            ClickHandler.Attach(btn, () =>
            {
                if (row.Cells == null) row.Cells = new List<KeyViewerCell>();
                row.Cells.Add(new KeyViewerCell { Token = token, WidthMul = 1f });
                rebuildRows();
            });
        }

        // ── Submenus ───────────────────────────────────────────────────────
        // Pushed on top of the editor view; the editor rebuilds on reveal, so deletes and
        // reorders show up when Back pops to it.

        private static void OpenRowSubmenu(
            KeyViewerPreset preset, bool isFoot, int rowIdx,
            Settings s, Action notify, Action rebuild)
        {
            _stack.Push(Loc.T("Row") + " " + (rowIdx + 1), body =>
            {
                var row = preset.Rows[rowIdx];

                UIBuilder.SectionHeader(body, "Row");
                UIBuilder.Slider(body, "Height", row.Height, 30f, 200f,
                    v => { row.Height = v; notify?.Invoke(); rebuild(); }, "0", 1f);
                UIBuilder.Collapsible(body, "Show rain", row.ShowRain,
                    v => { row.ShowRain = v; notify?.Invoke(); rebuild(); }, null);
                // 0 = follow the preset's Width step, which can't narrow the top row.
                UIBuilder.Slider(body, "Rain width (0 = auto)", row.RainWidth, 0f, 200f,
                    v => { row.RainWidth = v; notify?.Invoke(); rebuild(); }, "0", 1f);

                EnsureKv(ref row.RainColor, 1, 1, 1, 1);
                BindKv(body, "Rain color", row.RainColor,
                    () => { row.RainColorCustom = true; notify?.Invoke(); });

                UIBuilder.Spacer(body);
                // Only the live action gets the danger styling — arming a two-click confirm
                // on a button that can't do anything would just look broken.
                if (preset.Rows.Count > 1)
                    UIBuilder.DangerButton(body, "Delete this row", () =>
                    {
                        preset.Rows.RemoveAt(rowIdx);
                        notify?.Invoke();
                        rebuild();
                        _stack.Pop();
                    });
                else
                    UIBuilder.Button(body, "Delete this row (last row — disabled)", null);
            });
        }

        private static void OpenKeySubmenu(
            KeyViewerPreset preset, bool isFoot, int rowIdx, int cellIdx,
            Settings s, Action notify, Action rebuild)
        {
            _stack.Push(Loc.T("Row") + " " + (rowIdx + 1) + " / " + Loc.T("Cell") + " " + (cellIdx + 1), body =>
            {
                var cell = preset.Rows[rowIdx].Cells[cellIdx];

                UIBuilder.SectionHeader(body, "Key");

                // Bound-key display
                var tokenRow = UIBuilder.Rect("Token", body);
                var tokenLe = tokenRow.AddComponent<LayoutElement>();
                tokenLe.preferredHeight = UIBuilder.RowHeight;
                tokenLe.minHeight = UIBuilder.RowHeight;
                var tokenLblGo = UIBuilder.Rect("Lbl", tokenRow.transform);
                var tokenLblRect = (RectTransform)tokenLblGo.transform;
                tokenLblRect.anchorMin = new Vector2(0, 0);
                tokenLblRect.anchorMax = new Vector2(0, 1);
                tokenLblRect.pivot = new Vector2(0, 0.5f);
                tokenLblRect.sizeDelta = new Vector2(140f, 0);
                tokenLblRect.anchoredPosition = new Vector2(8f, 0);
                UIBuilder.Tmp(tokenLblGo, Loc.T("Bound key"), (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
                var tokenValGo = UIBuilder.Rect("Val", tokenRow.transform);
                var tokenValRect = (RectTransform)tokenValGo.transform;
                tokenValRect.anchorMin = new Vector2(1, 0);
                tokenValRect.anchorMax = new Vector2(1, 1);
                tokenValRect.pivot = new Vector2(1, 0.5f);
                tokenValRect.sizeDelta = new Vector2(220f, 0);
                tokenValRect.anchoredPosition = new Vector2(-8f, 0);
                UIBuilder.Tmp(tokenValGo, KeyTokens.PrettyTokenLabel(cell.Token), (int)UIBuilder.LabelFontSize, TextAnchor.MiddleRight, Theme.TextMuted);

                /* In-page rebind. The editor's rebind listener sits on the (now hidden)
                   editor view whose Update doesn't run, so the subpage carries its own.
                   A tester renamed every cell via the Label field believing it rebinds —
                   the binding needs a first-class control here, not just row right-click. */
                var listener = UIBuilder.Rect("CellRebindListener", body).AddComponent<KeyListener>();
                TMPro.TextMeshProUGUI bindBtnLabel = null;
                string bindPrompt = Loc.T("Change key — click, then press the new key");
                var bindBtn = UIBuilder.Button(body, bindPrompt, () =>
                {
                    listener.Active = true;
                    if (bindBtnLabel != null) bindBtnLabel.text = Loc.T("Press a key… (Esc cancels)");
                });
                bindBtnLabel = bindBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                listener.OnKey = kc =>
                {
                    listener.Active = false;
                    if (kc == KeyCode.Escape)
                    {
                        if (bindBtnLabel != null) bindBtnLabel.text = bindPrompt;
                        return;
                    }
                    // Same ordering as the row-grid rebind: swap the token first so
                    // TransferKeyCount's "old key still in use" scan sees the new binding.
                    bool hadOld = KeyViewer.TryParseKey(cell.Token, out KeyCode oldKey);
                    cell.Token = KeyTokens.TokenFromKeyCode(kc);
                    cell.Label = null; // clear stale display override
                    if (hadOld && KeyViewer.Instance != null)
                        KeyViewer.Instance.TransferKeyCount(preset, oldKey, kc);
                    notify?.Invoke();
                    rebuild();
                    _stack.RefreshTop(); // re-render bound key + cleared display text
                };

                UIBuilder.TextInput(body, "Display text", cell.Label ?? "",
                    v => { cell.Label = string.IsNullOrEmpty(v) ? null : v; notify?.Invoke(); rebuild(); });

                UIBuilder.Slider(body, "Width", cell.WidthMul, 0.25f, 4f,
                    v => { cell.WidthMul = v; notify?.Invoke(); rebuild(); }, "0.00");
                // 0 = follow the preset's Label size (Main → Label Text).
                UIBuilder.IntSlider(body, "Font size (0 = preset)", cell.LabelSize, 0, 48,
                    v => { cell.LabelSize = v; notify?.Invoke(); rebuild(); });

                UIBuilder.Spacer(body);
                UIBuilder.DangerButton(body, "Delete this cell", () =>
                {
                    preset.Rows[rowIdx].Cells.RemoveAt(cellIdx);
                    notify?.Invoke();
                    rebuild();
                    _stack.Pop();
                });
            });
        }

        // KvColor binding helpers — convert KvColor ↔ Color for the ColorPicker.
        private static void EnsureKv(ref KvColor c, float r, float g, float b, float a)
        {
            if (c == null) c = new KvColor { R = r, G = g, B = b, A = a };
        }

        private static GameObject BindKv(Transform parent, string label, KvColor col, Action notify)
        {
            var initial = new Color(col.R, col.G, col.B, col.A);
            return UIBuilder.ColorPicker(parent, label, initial, true, c =>
            {
                col.R = c.r; col.G = c.g; col.B = c.b; col.A = c.a;
                notify?.Invoke();
            });
        }
    }

    // Drag-reorder for cell buttons. A ghost clone follows the cursor (preserving grab
    // offset) while the original fades. On drop, the row strip under the cursor decides:
    // same row → reorder, different row → splice across rows, outside any row → no-op.
    internal class CellDragReorder : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public KeyViewerCell Cell;
        public KeyViewerRow Row;
        public KeyViewerPreset Preset;
        public RectTransform CellsContainer;
        public RectTransform GhostHost;
        public Action OnReorder;

        private GameObject _ghost;
        private RectTransform _ghostRt;
        private CanvasGroup _selfCg;
        private bool _dragging;
        // Cursor-to-cell-pivot vector at grab time, in WORLD coords so the per-frame
        // ScreenPointToWorldPointInRectangle conversion transparently handles CanvasScaler.
        private Vector3 _grabOffsetWorld;

        public void OnBeginDrag(PointerEventData e)
        {
            // Only respond to left-click drags. Right-click is rebind; middle is unused.
            if (e.button != PointerEventData.InputButton.Left) return;
            if (Row == null || Cell == null || CellsContainer == null || GhostHost == null) return;
            _dragging = true;

            // Compute grab offset in WORLD space — vector from cursor (converted to world
            // via ScreenPointToWorldPointInRectangle) to cell pivot.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    (RectTransform)transform.parent, e.position, e.pressEventCamera, out Vector3 cursorWorld))
            {
                _grabOffsetWorld = transform.position - cursorWorld;
            }

            _selfCg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _selfCg.alpha = 0.25f;
            _selfCg.blocksRaycasts = false;

            _ghost = UnityEngine.Object.Instantiate(gameObject, GhostHost);
            var ghostDr = _ghost.GetComponent<CellDragReorder>();
            if (ghostDr != null) UnityEngine.Object.Destroy(ghostDr);
            var ghostCg = _ghost.GetComponent<CanvasGroup>() ?? _ghost.AddComponent<CanvasGroup>();
            ghostCg.alpha = 0.9f;
            ghostCg.blocksRaycasts = false;
            var ghostLe = _ghost.GetComponent<LayoutElement>() ?? _ghost.AddComponent<LayoutElement>();
            ghostLe.ignoreLayout = true;

            _ghostRt = (RectTransform)_ghost.transform;
            UpdateGhostPosition(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || _ghost == null) return;
            UpdateGhostPosition(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;

            if (_selfCg != null)
            {
                _selfCg.alpha = 1f;
                _selfCg.blocksRaycasts = true;
            }
            if (_ghost != null)
            {
                UnityEngine.Object.Destroy(_ghost);
                _ghost = null;
            }

            if (Preset == null || Row == null || Cell == null || CellsContainer == null) return;

            var rowsContainer = CellsContainer.parent != null ? CellsContainer.parent.parent : null;
            if (rowsContainer == null) return;

            // Find which row's cells container the cursor is over. Sibling indices in
            // rowsContainer line up with Preset.Rows since they're built in order.
            RectTransform targetCells = null;
            int targetRowIdx = -1;
            for (int i = 0; i < rowsContainer.childCount && i < Preset.Rows.Count; i++)
            {
                var strip = rowsContainer.GetChild(i);
                var cellsT = strip.Find("Cells") as RectTransform;
                if (cellsT == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(cellsT, e.position, e.pressEventCamera))
                {
                    targetCells = cellsT;
                    targetRowIdx = i;
                    break;
                }
            }
            if (targetCells == null || targetRowIdx < 0) return;

            var targetRow = Preset.Rows[targetRowIdx];

            // Compare the cursor to each child's world center (GetWorldCorners) in world
            // space — the CanvasScaler mismatch made every drop land at index 0 otherwise.
            Vector3 cursorWorld;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    targetCells, e.position, e.pressEventCamera, out cursorWorld))
                return;

            Vector3[] corners = new Vector3[4];
            int targetIdx = 0;
            for (int i = 0; i < targetCells.childCount; i++)
            {
                var child = (RectTransform)targetCells.GetChild(i);
                if (child == transform) continue;
                child.GetWorldCorners(corners);
                float midX = (corners[0].x + corners[2].x) * 0.5f;
                if (cursorWorld.x > midX) targetIdx++;
            }

            int fromIdx = Row.Cells.IndexOf(Cell);
            if (fromIdx < 0) return;

            if (targetRow == Row)
            {
                if (targetIdx == fromIdx) return;
                Row.Cells.RemoveAt(fromIdx);
                // targetIdx already excludes the dragged cell (loop skips `child == transform`),
                // so it's the correct post-removal insertion index. No decrement.
                Row.Cells.Insert(Mathf.Clamp(targetIdx, 0, Row.Cells.Count), Cell);
            }
            else
            {
                Row.Cells.RemoveAt(fromIdx);
                if (targetRow.Cells == null) targetRow.Cells = new List<KeyViewerCell>();
                targetRow.Cells.Insert(Mathf.Clamp(targetIdx, 0, targetRow.Cells.Count), Cell);
            }
            OnReorder?.Invoke();
        }

        private void UpdateGhostPosition(PointerEventData e)
        {
            if (_ghost == null) return;
            var hostRt = (RectTransform)_ghost.transform.parent;
            // Convert cursor to world space against the ghost's parent rect. World coords
            // handle CanvasScaler correctly — screen pixels alone misalign under scaling.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    hostRt, e.position, e.pressEventCamera, out Vector3 cursorWorld))
            {
                _ghost.transform.position = cursorWorld + _grabOffsetWorld;
            }
        }
    }
}
